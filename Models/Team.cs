namespace pacman.Models;

public class Team
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFD700";

    public static readonly Team[] DefaultTeams =
    [
        new() { Name = "Yellow", Color = "#FFD700" },
        new() { Name = "Pink", Color = "#FFB8FF" },
        new() { Name = "Cyan", Color = "#00FFFF" },
        new() { Name = "Orange", Color = "#FFB852" }
    ];
}
