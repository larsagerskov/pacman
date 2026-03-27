using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using pacman.Models;

namespace pacman.Services;

public class GameManagerService(IWebHostEnvironment env, ILogger<GameManagerService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, GameState> _cache = new();

    private string DataDir => Path.Combine(env.ContentRootPath, "data");
    private string GamesDir => Path.Combine(DataDir, "games");
    private string IndexFile => Path.Combine(DataDir, "games.json");

    private static readonly string[] PlayerColors =
    [
        "#FFD700", "#FF6384", "#36A2EB", "#FF9F40", "#4BC0C0",
        "#9966FF", "#FF6633", "#00CC99", "#FF99CC", "#66CCFF",
        "#FFCC00", "#CC66FF", "#33CC33", "#FF3366", "#3399FF",
        "#FF6600", "#00CCCC", "#FF33CC", "#66FF33", "#CC3300",
        "#0099CC", "#FF9933", "#33CCFF", "#CC9900", "#6633FF",
        "#33FF99", "#CC3366", "#99FF33", "#3366CC", "#FF3300"
    ];

    // ==================== Index Operations ====================

    public async Task<GameIndex> LoadIndexAsync()
    {
        if (!File.Exists(IndexFile))
        {
            var defaultIndex = new GameIndex
            {
                AdminPassword = HashPassword("admin")
            };
            Directory.CreateDirectory(DataDir);
            await File.WriteAllTextAsync(IndexFile, JsonSerializer.Serialize(defaultIndex, JsonOpts));
            return defaultIndex;
        }
        return JsonSerializer.Deserialize<GameIndex>(await File.ReadAllTextAsync(IndexFile), JsonOpts)!;
    }

    private async Task SaveIndexAsync(GameIndex index)
    {
        await File.WriteAllTextAsync(IndexFile, JsonSerializer.Serialize(index, JsonOpts));
    }

    public bool ValidateAdmin(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        var index = LoadIndexAsync().GetAwaiter().GetResult();
        return index.AdminPassword == HashPassword(password);
    }

    private static string HashPassword(string password)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToHexStringLower(bytes);
    }

    // ==================== Game CRUD ====================

    public async Task<GameState> CreateGameAsync(string name, double centerLat, double centerLng, double radiusMeters, GameSettings? settings = null)
    {
        var id = GenerateGameId();
        var game = new GameState
        {
            Id = id,
            Name = name,
            CenterLat = centerLat,
            CenterLng = centerLng,
            RadiusMeters = radiusMeters,
            Settings = settings ?? new GameSettings(),
            CreatedAt = DateTime.UtcNow
        };

        if (game.Settings.TeamsEnabled)
        {
            game.Teams = Team.DefaultTeams.Take(game.Settings.TeamCount).ToList();
        }

        Directory.CreateDirectory(GamesDir);
        await SaveGameAsync(game);

        var index = await LoadIndexAsync();
        index.Games.Add(new GameSummary
        {
            Id = id,
            Name = name,
            Status = "waiting",
            CreatedAt = game.CreatedAt,
            CenterLat = centerLat,
            CenterLng = centerLng,
            RadiusMeters = radiusMeters
        });
        await SaveIndexAsync(index);

        logger.LogInformation("Created game {Id}: {Name}", id, name);
        return game;
    }

    public async Task<GameState?> GetGameAsync(string gameId)
    {
        if (_cache.TryGetValue(gameId, out var cached))
            return cached;

        var file = Path.Combine(GamesDir, $"{gameId}.json");
        if (!File.Exists(file)) return null;

        var game = JsonSerializer.Deserialize<GameState>(await File.ReadAllTextAsync(file), JsonOpts)!;
        _cache[gameId] = game;
        return game;
    }

    public async Task SaveGameAsync(GameState game)
    {
        var fileLock = _locks.GetOrAdd(game.Id, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
        try
        {
            var file = Path.Combine(GamesDir, $"{game.Id}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(game, JsonOpts));
            _cache[game.Id] = game;
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task DeleteGameAsync(string gameId)
    {
        var file = Path.Combine(GamesDir, $"{gameId}.json");
        if (File.Exists(file)) File.Delete(file);
        _cache.TryRemove(gameId, out _);
        _locks.TryRemove(gameId, out _);

        var index = await LoadIndexAsync();
        index.Games.RemoveAll(g => g.Id == gameId);
        await SaveIndexAsync(index);
    }

    // ==================== Game Flow ====================

    public async Task<GameState> SetDotsAsync(string gameId, List<Dot> dots, List<PowerUp> powerUps, List<RoadSegment> roads)
    {
        var game = await GetGameAsync(gameId) ?? throw new KeyNotFoundException(gameId);
        game.Dots = dots;
        game.PowerUps = powerUps;
        game.Roads = roads;

        if (game.Settings.GhostsEnabled)
        {
            game.Ghosts = Ghost.Templates.Select(t => new Ghost
            {
                Name = t.Name,
                Color = t.Color,
                Lat = game.CenterLat,
                Lng = game.CenterLng
            }).ToList();
        }

        await SaveGameAsync(game);
        await UpdateIndexSummary(gameId, g => g.DotCount = dots.Count + powerUps.Count);
        return game;
    }

    public async Task<GameState> StartGameAsync(string gameId)
    {
        var game = await GetGameAsync(gameId) ?? throw new KeyNotFoundException(gameId);
        game.Status = "active";
        game.StartedAt = DateTime.UtcNow;

        if (game.Settings.EndCondition == "time")
            game.EndsAt = game.StartedAt.Value.AddMinutes(game.Settings.TimeLimitMinutes);

        await SaveGameAsync(game);
        await UpdateIndexSummary(gameId, g => g.Status = "active");
        return game;
    }

    public async Task<GameState> StopGameAsync(string gameId)
    {
        var game = await GetGameAsync(gameId) ?? throw new KeyNotFoundException(gameId);
        game.Status = "finished";
        game.EndsAt = DateTime.UtcNow;
        await SaveGameAsync(game);
        await UpdateIndexSummary(gameId, g => g.Status = "finished");
        return game;
    }

    public async Task<GameState> ResetGameAsync(string gameId)
    {
        var game = await GetGameAsync(gameId) ?? throw new KeyNotFoundException(gameId);
        game.Status = "waiting";
        game.StartedAt = null;
        game.EndsAt = null;
        game.BonusItems.Clear();

        foreach (var dot in game.Dots)
        {
            dot.Collected = false;
            dot.CollectedBy = null;
            dot.CollectedAt = null;
        }
        foreach (var pu in game.PowerUps)
        {
            pu.Collected = false;
            pu.CollectedBy = null;
            pu.CollectedAt = null;
        }
        foreach (var player in game.Players)
        {
            player.Score = 0;
            player.DotsEaten = 0;
            player.PowerUpsUsed = 0;
            player.BonusesCollected = 0;
            player.ActivePowerUps.Clear();
            player.Frozen = false;
            player.FrozenUntil = null;
        }

        await SaveGameAsync(game);
        await UpdateIndexSummary(gameId, g => g.Status = "waiting");
        return game;
    }

    // ==================== Player Operations ====================

    public async Task<Player> JoinGameAsync(string gameId, string playerName, string? team = null)
    {
        var game = await GetGameAsync(gameId) ?? throw new KeyNotFoundException(gameId);

        if (game.Players.Any(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Name already taken");

        var colorIndex = game.Players.Count % PlayerColors.Length;
        var player = new Player
        {
            Name = playerName,
            Color = game.Settings.TeamsEnabled && team != null
                ? game.Teams.FirstOrDefault(t => t.Name == team)?.Color ?? PlayerColors[colorIndex]
                : PlayerColors[colorIndex],
            Team = team,
            LastSeen = DateTime.UtcNow
        };

        game.Players.Add(player);
        await SaveGameAsync(game);
        await UpdateIndexSummary(gameId, g => g.PlayerCount = game.Players.Count);

        return player;
    }

    public async Task UpdatePlayerPositionAsync(string gameId, string playerName, double lat, double lng)
    {
        var game = await GetGameAsync(gameId);
        if (game == null) return;

        var player = game.Players.FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
        if (player == null) return;

        player.Lat = lat;
        player.Lng = lng;
        player.LastSeen = DateTime.UtcNow;

        // Clean up expired power-ups
        player.ActivePowerUps.RemoveAll(pu => pu.ExpiresAt <= DateTime.UtcNow);

        // Clean up freeze
        if (player.Frozen && player.FrozenUntil.HasValue && player.FrozenUntil.Value <= DateTime.UtcNow)
        {
            player.Frozen = false;
            player.FrozenUntil = null;
        }

        // Don't save to disk on every position update - too frequent
        // Cache is updated in-memory
    }

    /// <summary>
    /// Check and collect all dots within range of player.
    /// Returns list of collected dot IDs and points earned.
    /// </summary>
    public async Task<(List<string> dotIds, List<string> powerUpIds, int pointsEarned)> CollectNearbyAsync(
        string gameId, string playerName, double lat, double lng)
    {
        var game = await GetGameAsync(gameId);
        if (game == null || game.Status != "active") return ([], [], 0);

        var player = game.Players.FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
        if (player == null || player.Frozen) return ([], [], 0);

        var collectedDots = new List<string>();
        var collectedPowerUps = new List<string>();
        int points = 0;

        // Determine collection radius (magnet power-up doubles it)
        var radius = game.Settings.CollectionRadius;
        if (player.ActivePowerUps.Any(pu => pu.Type == "magnet" && pu.ExpiresAt > DateTime.UtcNow))
            radius *= 2;

        // Score multiplier
        var multiplier = player.ActivePowerUps.Any(pu => pu.Type == "multiplier" && pu.ExpiresAt > DateTime.UtcNow) ? 2 : 1;

        // Collect dots
        foreach (var dot in game.Dots.Where(d => !d.Collected))
        {
            if (GeoMath.HaversineDistance(lat, lng, dot.Lat, dot.Lng) <= radius)
            {
                dot.Collected = true;
                dot.CollectedBy = playerName;
                dot.CollectedAt = DateTime.UtcNow;
                collectedDots.Add(dot.Id);
                points += game.Settings.DotPoints * multiplier;
                player.DotsEaten++;
            }
        }

        // Collect power-ups
        foreach (var pu in game.PowerUps.Where(p => !p.Collected))
        {
            if (GeoMath.HaversineDistance(lat, lng, pu.Lat, pu.Lng) <= radius)
            {
                pu.Collected = true;
                pu.CollectedBy = playerName;
                pu.CollectedAt = DateTime.UtcNow;
                collectedPowerUps.Add(pu.Id);
                player.PowerUpsUsed++;

                player.ActivePowerUps.Add(new ActivePowerUp
                {
                    Type = pu.Type,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(
                        pu.Type is "ghost" or "steal"
                            ? 120  // 2 min for ghost mode and steal
                            : game.Settings.PowerUpDurationSeconds) // 3 min for others
                });
            }
        }

        // Collect bonus items
        foreach (var bonus in game.BonusItems.Where(b => !b.Collected && b.ExpiresAt > DateTime.UtcNow))
        {
            if (GeoMath.HaversineDistance(lat, lng, bonus.Lat, bonus.Lng) <= radius)
            {
                bonus.Collected = true;
                bonus.CollectedBy = playerName;
                points += bonus.Points * multiplier;
                player.BonusesCollected++;
            }
        }

        if (points > 0)
        {
            player.Score += points;
            await SaveGameAsync(game);
        }

        // Check steal power-up against other players
        if (player.ActivePowerUps.Any(pu => pu.Type == "steal" && pu.ExpiresAt > DateTime.UtcNow))
        {
            foreach (var other in game.Players.Where(p => p.Name != playerName && p.Lat.HasValue))
            {
                if (GeoMath.HaversineDistance(lat, lng, other.Lat!.Value, other.Lng!.Value) <= 15)
                {
                    var stolen = (int)(other.Score * 0.10);
                    if (stolen > 0)
                    {
                        other.Score -= stolen;
                        player.Score += stolen;
                        points += stolen;
                    }
                }
            }
        }

        // Check if all dots eaten (end condition)
        if (game.Settings.EndCondition == "allDots" &&
            game.Dots.All(d => d.Collected) &&
            game.PowerUps.All(p => p.Collected))
        {
            game.Status = "finished";
            game.EndsAt = DateTime.UtcNow;
        }

        return (collectedDots, collectedPowerUps, points);
    }

    // ==================== Scoreboard ====================

    public async Task<object> GetScoreboardAsync(string gameId)
    {
        var game = await GetGameAsync(gameId);
        if (game == null) return new { };

        if (game.Settings.TeamsEnabled)
        {
            var teamScores = game.Teams.Select(t => new
            {
                t.Name,
                t.Color,
                Score = game.Players.Where(p => p.Team == t.Name).Sum(p => p.Score),
                DotsEaten = game.Players.Where(p => p.Team == t.Name).Sum(p => p.DotsEaten),
                Players = game.Players.Where(p => p.Team == t.Name)
                    .OrderByDescending(p => p.Score)
                    .Select(p => new { p.Name, p.Score, p.DotsEaten })
            }).OrderByDescending(t => t.Score).ToList();

            return new { teams = true, teamScores, totalDots = game.Dots.Count + game.PowerUps.Count };
        }

        var players = game.Players
            .OrderByDescending(p => p.Score)
            .Select(p => new { p.Name, p.Score, p.DotsEaten, p.PowerUpsUsed, p.BonusesCollected, p.Color, p.Team })
            .ToList();

        return new { teams = false, players, totalDots = game.Dots.Count + game.PowerUps.Count };
    }

    // ==================== Helpers ====================

    private static string GenerateGameId()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I,O,0,1 to avoid confusion
        var rng = new Random();
        return new string(Enumerable.Range(0, 6).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
    }

    private async Task UpdateIndexSummary(string gameId, Action<GameSummary> update)
    {
        var index = await LoadIndexAsync();
        var summary = index.Games.FirstOrDefault(g => g.Id == gameId);
        if (summary != null)
        {
            update(summary);
            await SaveIndexAsync(index);
        }
    }
}
