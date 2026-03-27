using Microsoft.AspNetCore.SignalR;
using pacman.Services;

namespace pacman.Hubs;

public class PacmanHub(GameManagerService gameManager) : Hub
{
    private static int _connectionCount;

    public override async Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connectionCount);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connectionCount);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGame(string gameId, string playerName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
        await Clients.Group(gameId).SendAsync("PlayerJoined", playerName);
    }

    public async Task LeaveGame(string gameId, string playerName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);
        await Clients.Group(gameId).SendAsync("PlayerLeft", playerName);
    }

    public async Task UpdatePosition(string gameId, string playerName, double lat, double lng)
    {
        await gameManager.UpdatePlayerPositionAsync(gameId, playerName, lat, lng);

        // Broadcast position to other players
        await Clients.OthersInGroup(gameId).SendAsync("PlayerMoved", new
        {
            Name = playerName,
            Lat = lat,
            Lng = lng
        });

        // Check for dot collection
        var (dotIds, powerUpIds, points) = await gameManager.CollectNearbyAsync(gameId, playerName, lat, lng);

        if (dotIds.Count > 0 || powerUpIds.Count > 0)
        {
            var game = await gameManager.GetGameAsync(gameId);
            var player = game?.Players.FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            await Clients.Group(gameId).SendAsync("DotsCollected", new
            {
                PlayerName = playerName,
                DotIds = dotIds,
                PowerUpIds = powerUpIds,
                PointsEarned = points,
                TotalScore = player?.Score ?? 0
            });

            // Check if game ended
            if (game?.Status == "finished")
            {
                var scoreboard = await gameManager.GetScoreboardAsync(gameId);
                await Clients.Group(gameId).SendAsync("GameEnded", scoreboard);
            }
        }
    }
}
