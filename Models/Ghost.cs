namespace pacman.Models;

public class Ghost
{
    public string Name { get; set; } = ""; // "blinky", "pinky", "inky", "clyde"
    public string Color { get; set; } = "#FF0000";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double Speed { get; set; } = 1.2; // meters per second (walking speed)
    public int CurrentRoadIndex { get; set; }
    public double ProgressAlongRoad { get; set; }
    public bool IsEaten { get; set; }
    public DateTime? RespawnAt { get; set; }

    public static readonly Ghost[] Templates =
    [
        new() { Name = "blinky", Color = "#FF0000" },
        new() { Name = "pinky", Color = "#FFB8FF" },
        new() { Name = "inky", Color = "#00FFFF" },
        new() { Name = "clyde", Color = "#FFB852" }
    ];
}

public class RoadSegment
{
    public List<double[]> Coordinates { get; set; } = new(); // [lat, lng] pairs
    public double Length { get; set; }
    public List<int> ConnectedSegments { get; set; } = new();
}
