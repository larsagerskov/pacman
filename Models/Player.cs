namespace pacman.Models;

public class Player
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public int DotsEaten { get; set; }
    public int PowerUpsUsed { get; set; }
    public int BonusesCollected { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? Team { get; set; }
    public string Color { get; set; } = "#FFD700";
    public List<ActivePowerUp> ActivePowerUps { get; set; } = new();
    public bool Frozen { get; set; }
    public DateTime? FrozenUntil { get; set; }
}
