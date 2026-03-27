namespace pacman.Models;

public class PowerUp
{
    public string Id { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Type { get; set; } = "multiplier"; // "multiplier", "magnet", "ghost", "steal"
    public bool Collected { get; set; }
    public string? CollectedBy { get; set; }
    public DateTime? CollectedAt { get; set; }
}

public class ActivePowerUp
{
    public string Type { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
