namespace pacman.Models;

public class GameState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "waiting"; // "waiting", "active", "finished"
    public GameSettings Settings { get; set; } = new();
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double RadiusMeters { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public List<Dot> Dots { get; set; } = new();
    public List<PowerUp> PowerUps { get; set; } = new();
    public List<BonusItem> BonusItems { get; set; } = new();
    public List<Player> Players { get; set; } = new();
    public List<Ghost> Ghosts { get; set; } = new();
    public List<RoadSegment> Roads { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
}

public class GameIndex
{
    public string AdminPassword { get; set; } = "";
    public List<GameSummary> Games { get; set; } = new();
}

public class GameSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "waiting";
    public DateTime CreatedAt { get; set; }
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double RadiusMeters { get; set; }
    public int DotCount { get; set; }
    public int PlayerCount { get; set; }
}
