namespace Butthurt.Contracts.Models;

public class RoadmapItem
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Emoji { get; set; } = "🚀";

    // Computed property for convenience
    public string DisplayTitle => $"{Emoji} {Title}";
}

