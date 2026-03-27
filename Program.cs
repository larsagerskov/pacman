using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using pacman.Hubs;
using pacman.Models;
using pacman.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHttpClient("overpass", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PacmanGPS/1.0");
});

builder.Services.AddSingleton<GameManagerService>();
builder.Services.AddSingleton<OverpassService>();
builder.Services.AddSingleton<DotGeneratorService>();
builder.Services.AddHostedService<BonusSpawnerService>();
builder.Services.AddHostedService<GhostService>();

var app = builder.Build();

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapHub<PacmanHub>("/pacmanHub");

var gameManager = app.Services.GetRequiredService<GameManagerService>();
var dotGenerator = app.Services.GetRequiredService<DotGeneratorService>();
var hubContext = app.Services.GetRequiredService<IHubContext<PacmanHub>>();

bool ValidateAdmin(HttpRequest request) =>
    gameManager.ValidateAdmin(request.Headers["X-Admin-Password"].FirstOrDefault());

// ==================== Public API ====================

app.MapGet("/api/games/{id}", async (string id) =>
{
    var game = await gameManager.GetGameAsync(id);
    if (game == null) return Results.NotFound();
    return Results.Json(new
    {
        game.Id,
        game.Name,
        game.Status,
        game.CenterLat,
        game.CenterLng,
        game.RadiusMeters,
        game.Settings,
        game.StartedAt,
        game.EndsAt,
        DotCount = game.Dots.Count,
        PowerUpCount = game.PowerUps.Count,
        PlayerCount = game.Players.Count,
        Teams = game.Teams
    }, jsonOpts);
});

app.MapGet("/api/games/{id}/dots", async (string id) =>
{
    var game = await gameManager.GetGameAsync(id);
    if (game == null) return Results.NotFound();
    return Results.Json(new
    {
        dots = game.Dots.Select(d => new { d.Id, d.Lat, d.Lng, d.Collected, d.CollectedBy }),
        powerUps = game.PowerUps.Select(p => new { p.Id, p.Lat, p.Lng, p.Type, p.Collected, p.CollectedBy }),
        bonusItems = game.BonusItems.Where(b => !b.Collected && b.ExpiresAt > DateTime.UtcNow)
            .Select(b => new { b.Id, b.Lat, b.Lng, b.Type, b.Points, b.ExpiresAt }),
        ghosts = game.Ghosts.Where(g => !g.IsEaten).Select(g => new { g.Name, g.Color, g.Lat, g.Lng })
    }, jsonOpts);
});

app.MapGet("/api/games/{id}/scoreboard", async (string id) =>
{
    var scoreboard = await gameManager.GetScoreboardAsync(id);
    return Results.Json(scoreboard, jsonOpts);
});

app.MapPost("/api/games/{id}/join", async (string id, HttpRequest request) =>
{
    var body = await request.ReadFromJsonAsync<JoinRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Name is required");

    try
    {
        var player = await gameManager.JoinGameAsync(id, body.Name.Trim(), body.Team);
        return Results.Json(player, jsonOpts);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound("Game not found");
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(ex.Message);
    }
});

app.MapPost("/api/games/{id}/collect", async (string id, HttpRequest request) =>
{
    var body = await request.ReadFromJsonAsync<CollectRequest>();
    if (body == null) return Results.BadRequest();

    var (dotIds, powerUpIds, points) = await gameManager.CollectNearbyAsync(id, body.Name, body.Lat, body.Lng);
    return Results.Json(new { dotIds, powerUpIds, points }, jsonOpts);
});

app.MapPost("/api/games/{id}/heartbeat", async (string id, HttpRequest request) =>
{
    var body = await request.ReadFromJsonAsync<HeartbeatRequest>();
    if (body == null) return Results.BadRequest();

    await gameManager.UpdatePlayerPositionAsync(id, body.Name, body.Lat, body.Lng);
    return Results.Ok();
});

// ==================== Admin API ====================

app.MapPost("/api/admin/login", (HttpRequest request) =>
{
    return ValidateAdmin(request) ? Results.Ok() : Results.Unauthorized();
});

app.MapGet("/api/admin/games", async (HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();
    var index = await gameManager.LoadIndexAsync();
    return Results.Json(index.Games, jsonOpts);
});

app.MapPost("/api/admin/games", async (HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();

    var body = await request.ReadFromJsonAsync<CreateGameRequest>(jsonOpts);
    if (body == null || string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Name is required");

    var game = await gameManager.CreateGameAsync(body.Name, body.CenterLat, body.CenterLng, body.RadiusMeters, body.Settings);

    // Generate dots from road data
    try
    {
        var (dots, powerUps, roads) = await dotGenerator.GenerateAsync(body.CenterLat, body.CenterLng, body.RadiusMeters);
        await gameManager.SetDotsAsync(game.Id, dots, powerUps, roads);

        return Results.Json(new
        {
            game.Id,
            game.Name,
            DotCount = dots.Count,
            PowerUpCount = powerUps.Count,
            RoadCount = roads.Count
        }, jsonOpts);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to generate dots: {ex.Message}");
    }
});

app.MapPost("/api/admin/games/{id}/start", async (string id, HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();
    var game = await gameManager.StartGameAsync(id);
    await hubContext.Clients.Group(id).SendAsync("GameStarted", new { game.StartedAt, game.EndsAt });
    return Results.Json(new { game.Status, game.StartedAt, game.EndsAt }, jsonOpts);
});

app.MapPost("/api/admin/games/{id}/stop", async (string id, HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();
    var game = await gameManager.StopGameAsync(id);
    var scoreboard = await gameManager.GetScoreboardAsync(id);
    await hubContext.Clients.Group(id).SendAsync("GameEnded", scoreboard);
    return Results.Json(new { game.Status }, jsonOpts);
});

app.MapPost("/api/admin/games/{id}/reset", async (string id, HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();
    await gameManager.ResetGameAsync(id);
    await hubContext.Clients.Group(id).SendAsync("GameReset");
    return Results.Ok();
});

app.MapDelete("/api/admin/games/{id}", async (string id, HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();
    await gameManager.DeleteGameAsync(id);
    return Results.Ok();
});

app.MapPut("/api/admin/games/{id}/settings", async (string id, HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();
    var settings = await request.ReadFromJsonAsync<GameSettings>(jsonOpts);
    if (settings == null) return Results.BadRequest();

    var game = await gameManager.GetGameAsync(id);
    if (game == null) return Results.NotFound();

    game.Settings = settings;
    if (settings.TeamsEnabled && game.Teams.Count == 0)
        game.Teams = Team.DefaultTeams.Take(settings.TeamCount).ToList();

    await gameManager.SaveGameAsync(game);
    return Results.Ok();
});

app.MapGet("/api/admin/games/{id}/players", async (string id, HttpRequest request) =>
{
    if (!ValidateAdmin(request)) return Results.Unauthorized();
    var game = await gameManager.GetGameAsync(id);
    if (game == null) return Results.NotFound();
    return Results.Json(game.Players, jsonOpts);
});

// ==================== Startup Banner ====================

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine("\n=== PACMAN GPS ===");
    Console.WriteLine($"Server running on: {string.Join(", ", app.Urls)}");

    try
    {
        var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                     && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString());

        foreach (var ip in networkInterfaces)
            Console.WriteLine($"  Network: http://{ip}:5000");
    }
    catch { }

    Console.WriteLine("==================\n");
});

app.Run();

// ==================== Request Models ====================

record JoinRequest(string Name, string? Team);
record CollectRequest(string Name, double Lat, double Lng);
record HeartbeatRequest(string Name, double Lat, double Lng);
record CreateGameRequest(string Name, double CenterLat, double CenterLng, double RadiusMeters, GameSettings? Settings);
