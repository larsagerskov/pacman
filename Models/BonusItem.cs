namespace pacman.Models;

public class BonusItem
{
    public string Id { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Type { get; set; } = "cherry"; // "cherry", "strawberry", "orange", "apple", "melon"
    public int Points { get; set; } = 100;
    public DateTime SpawnedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Collected { get; set; }
    public string? CollectedBy { get; set; }

    public static readonly Dictionary<string, int> PointValues = new()
    {
        ["cherry"] = 100,
        ["strawberry"] = 300,
        ["orange"] = 500,
        ["apple"] = 700,
        ["melon"] = 1000
    };

    public static readonly Dictionary<string, double> SpawnWeights = new()
    {
        ["cherry"] = 0.40,
        ["strawberry"] = 0.30,
        ["orange"] = 0.15,
        ["apple"] = 0.10,
        ["melon"] = 0.05
    };
}
