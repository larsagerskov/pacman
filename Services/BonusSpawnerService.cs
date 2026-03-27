using Microsoft.AspNetCore.SignalR;
using pacman.Hubs;
using pacman.Models;

namespace pacman.Services;

public class BonusSpawnerService(
    GameManagerService gameManager,
    IHubContext<PacmanHub> hubContext,
    ILogger<BonusSpawnerService> logger) : BackgroundService
{
    private static readonly Random Rng = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(30_000, stoppingToken); // Check every 30 seconds

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
                logger.LogError(ex, "Error in bonus spawner");
            }
        }
    }

    private async Task ProcessGame(string gameId)
    {
        var game = await gameManager.GetGameAsync(gameId);
        if (game == null || game.Status != "active") return;

        // Remove expired bonuses
        var expired = game.BonusItems.Where(b => !b.Collected && b.ExpiresAt <= DateTime.UtcNow).ToList();
        foreach (var bonus in expired)
        {
            game.BonusItems.Remove(bonus);
            await hubContext.Clients.Group(gameId).SendAsync("BonusDespawned", bonus.Id);
        }

        // Spawn new bonus if interval has passed
        var lastSpawn = game.BonusItems
            .Where(b => b.SpawnedAt > DateTime.MinValue)
            .OrderByDescending(b => b.SpawnedAt)
            .FirstOrDefault()?.SpawnedAt ?? game.StartedAt ?? DateTime.UtcNow.AddMinutes(-10);

        var interval = game.Settings.BonusSpawnIntervalSeconds + Rng.Next(-60, 60); // Add randomness
        if ((DateTime.UtcNow - lastSpawn).TotalSeconds < Math.Max(60, interval)) return;

        // Pick a random uncollected dot location for the bonus
        var availableDots = game.Dots.Where(d => !d.Collected).ToList();
        if (availableDots.Count == 0) return;

        var targetDot = availableDots[Rng.Next(availableDots.Count)];

        // Pick bonus type based on weighted probability
        var bonusType = PickBonusType();
        var bonusId = $"b-{game.BonusItems.Count + 1:D4}";

        var bonus2 = new BonusItem
        {
            Id = bonusId,
            Lat = targetDot.Lat,
            Lng = targetDot.Lng,
            Type = bonusType,
            Points = BonusItem.PointValues[bonusType],
            SpawnedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(60)
        };

        game.BonusItems.Add(bonus2);
        await gameManager.SaveGameAsync(game);

        await hubContext.Clients.Group(gameId).SendAsync("BonusSpawned", new
        {
            bonus2.Id,
            bonus2.Lat,
            bonus2.Lng,
            bonus2.Type,
            bonus2.Points,
            bonus2.ExpiresAt
        });

        logger.LogInformation("Spawned {Type} bonus in game {GameId}", bonusType, gameId);
    }

    private static string PickBonusType()
    {
        var roll = Rng.NextDouble();
        double cumulative = 0;
        foreach (var (type, weight) in BonusItem.SpawnWeights)
        {
            cumulative += weight;
            if (roll <= cumulative) return type;
        }
        return "cherry";
    }
}
