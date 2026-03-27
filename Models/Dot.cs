namespace pacman.Models;

public class Dot
{
    public string Id { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public bool Collected { get; set; }
    public string? CollectedBy { get; set; }
    public DateTime? CollectedAt { get; set; }
}
