using Microsoft.AspNetCore.SignalR;
using pacman.Hubs;
using pacman.Models;

namespace pacman.Services;

public class GhostService(
    GameManagerService gameManager,
    IHubContext<PacmanHub> hubContext,
    ILogger<GhostService> logger) : BackgroundService
{
    private static readonly Random Rng = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(2_000, stoppingToken); // Update every 2 seconds

            try
            {
                var index = await gameManager.LoadIndexAsync();
                foreach (var summary in index.Games.Where(g => g.Status == "active"))
                {
                    await ProcessGame(summary.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ghost service");
            }
        }
    }

    private async Task ProcessGame(string gameId)
    {
        var game = await gameManager.GetGameAsync(gameId);
        if (game == null || game.Status != "active" || !game.Settings.GhostsEnabled) return;
        if (game.Roads.Count == 0 || game.Ghosts.Count == 0) return;

        var activePlayers = game.Players
            .Where(p => p.Lat.HasValue && p.LastSeen > DateTime.UtcNow.AddMinutes(-2))
            .ToList();

        foreach (var ghost in game.Ghosts)
        {
            // Handle respawn
            if (ghost.IsEaten)
            {
                if (ghost.RespawnAt.HasValue && ghost.RespawnAt.Value <= DateTime.UtcNow)
                {
                    ghost.IsEaten = false;
                    ghost.RespawnAt = null;
                    // Respawn at random road position
                    var randomRoad = game.Roads[Rng.Next(game.Roads.Count)];
                    var midIdx = randomRoad.Coordinates.Count / 2;
                    ghost.Lat = randomRoad.Coordinates[midIdx][0];
                    ghost.Lng = randomRoad.Coordinates[midIdx][1];
                    ghost.CurrentRoadIndex = game.Roads.IndexOf(randomRoad);
                    ghost.ProgressAlongRoad = 0.5;
                }
                continue;
            }

            MoveGhost(ghost, game, activePlayers);

            // Check if ghost catches any player
            foreach (var player in activePlayers)
            {
                if (player.Frozen) continue;

                var dist = GeoMath.HaversineDistance(ghost.Lat, ghost.Lng, player.Lat!.Value, player.Lng!.Value);
                if (dist > 10) continue;

                // Check if player has steal power-up (can eat ghosts)
                if (player.ActivePowerUps.Any(pu => pu.Type == "steal" && pu.ExpiresAt > DateTime.UtcNow))
                {
                    ghost.IsEaten = true;
                    ghost.RespawnAt = DateTime.UtcNow.AddSeconds(30);
                    player.Score += 200;

                    await hubContext.Clients.Group(gameId).SendAsync("GhostEaten", new
                    {
                        GhostName = ghost.Name,
                        PlayerName = player.Name,
                        Points = 200
                    });

                    logger.LogInformation("Ghost {Ghost} eaten by {Player} in game {GameId}", ghost.Name, player.Name, gameId);
                }
                else
                {
                    // Ghost catches player
                    ApplyGhostEffect(player, game.Settings);

                    await hubContext.Clients.Group(gameId).SendAsync("GhostCaughtPlayer", new
                    {
                        ghost.Name,
                        PlayerName = player.Name,
                        game.Settings.GhostEffect
                    });

                    logger.LogInformation("Ghost {Ghost} caught {Player} in game {GameId}", ghost.Name, player.Name, gameId);
                }
            }
        }

        // Broadcast ghost positions
        var ghostData = game.Ghosts.Where(g => !g.IsEaten).Select(g => new
        {
            g.Name,
            g.Color,
            g.Lat,
            g.Lng,
            g.IsEaten
        });

        await hubContext.Clients.Group(gameId).SendAsync("GhostMoved", ghostData);
    }

    private static void MoveGhost(Ghost ghost, GameState game, List<Player> activePlayers)
    {
        if (game.Roads.Count == 0) return;

        // Ensure valid road index
        if (ghost.CurrentRoadIndex < 0 || ghost.CurrentRoadIndex >= game.Roads.Count)
            ghost.CurrentRoadIndex = Rng.Next(game.Roads.Count);

        var road = game.Roads[ghost.CurrentRoadIndex];
        if (road.Coordinates.Count < 2) return;

        // Move along current road (speed = ~1.2 m/s, update every 2s = ~2.4m per tick)
        var moveDistance = ghost.Speed * 2.0;
        var roadLen = road.Length;
        if (roadLen < 1) return;

        ghost.ProgressAlongRoad += moveDistance / roadLen;

        // If reached end of road, pick next road
        if (ghost.ProgressAlongRoad >= 1.0 || ghost.ProgressAlongRoad <= 0.0)
        {
            ghost.ProgressAlongRoad = Math.Clamp(ghost.ProgressAlongRoad, 0.0, 1.0);

            if (road.ConnectedSegments.Count > 0)
            {
                // Bias toward roads that lead toward nearest player
                var nextRoad = PickNextRoad(ghost, game, activePlayers);
                ghost.CurrentRoadIndex = nextRoad;
                ghost.ProgressAlongRoad = 0.01;
            }
            else
            {
                // Dead end: reverse
                ghost.ProgressAlongRoad = 0.99;
                ghost.Speed = -ghost.Speed;
            }
        }

        // Interpolate position along road
        var progress = Math.Clamp(ghost.ProgressAlongRoad, 0, 1);
        var totalLen = road.Length;
        var targetDist = progress * totalLen;
        double accumulated = 0;

        for (int i = 0; i < road.Coordinates.Count - 1; i++)
        {
            var segLen = GeoMath.HaversineDistance(
                road.Coordinates[i][0], road.Coordinates[i][1],
                road.Coordinates[i + 1][0], road.Coordinates[i + 1][1]);

            if (accumulated + segLen >= targetDist)
            {
                var t = (targetDist - accumulated) / segLen;
                ghost.Lat = road.Coordinates[i][0] + t * (road.Coordinates[i + 1][0] - road.Coordinates[i][0]);
                ghost.Lng = road.Coordinates[i][1] + t * (road.Coordinates[i + 1][1] - road.Coordinates[i][1]);
                break;
            }
            accumulated += segLen;
        }
    }

    private static int PickNextRoad(Ghost ghost, GameState game, List<Player> activePlayers)
    {
        var road = game.Roads[ghost.CurrentRoadIndex];
        var candidates = road.ConnectedSegments;

        if (candidates.Count == 0) return ghost.CurrentRoadIndex;
        if (activePlayers.Count == 0) return candidates[Rng.Next(candidates.Count)];

        // Find nearest player
        var nearest = activePlayers
            .OrderBy(p => GeoMath.HaversineDistance(ghost.Lat, ghost.Lng, p.Lat!.Value, p.Lng!.Value))
            .First();

        // 70% chance to move toward nearest player, 30% random
        if (Rng.NextDouble() < 0.7)
        {
            return candidates
                .OrderBy(idx =>
                {
                    var mid = game.Roads[idx].Coordinates[game.Roads[idx].Coordinates.Count / 2];
                    return GeoMath.HaversineDistance(mid[0], mid[1], nearest.Lat!.Value, nearest.Lng!.Value);
                })
                .First();
        }

        return candidates[Rng.Next(candidates.Count)];
    }

    private static void ApplyGhostEffect(Player player, GameSettings settings)
    {
        switch (settings.GhostEffect)
        {
            case "points":
                player.Score = Math.Max(0, player.Score - (int)(player.Score * 0.20));
                break;
            case "freeze":
                player.Frozen = true;
                player.FrozenUntil = DateTime.UtcNow.AddSeconds(30);
                break;
            case "both":
                player.Score = Math.Max(0, player.Score - (int)(player.Score * 0.10));
                player.Frozen = true;
                player.FrozenUntil = DateTime.UtcNow.AddSeconds(20);
                break;
        }
    }
}
