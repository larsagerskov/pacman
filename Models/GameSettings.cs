namespace pacman.Models;

public class GameSettings
{
    public double CollectionRadius { get; set; } = 10;
    public int DotPoints { get; set; } = 10;
    public int PowerUpDurationSeconds { get; set; } = 180;
    public int TimeLimitMinutes { get; set; } = 60;
    public string EndCondition { get; set; } = "time"; // "time", "allDots", "manual"
    public int BonusSpawnIntervalSeconds { get; set; } = 180;
    public bool GhostsEnabled { get; set; } = false;
    public string GhostEffect { get; set; } = "points"; // "points", "freeze", "both"
    public bool TeamsEnabled { get; set; } = false;
    public int TeamCount { get; set; } = 2;
    public int PowerUpCycleSeconds { get; set; } = 20;
}
