using System.Collections.ObjectModel;
using Butthurt.Contracts.Models;
namespace Butthurt.Contracts.ViewModels;

public class RoadmapViewModel
{


    // ObservableCollection allows the UI to update automatically when items are added
    public ObservableCollection<RoadmapItem> Items { get; } = new();

    public RoadmapViewModel()
    {
        // Example static items (can later load from a file or API)
        Items.Add(new RoadmapItem { Emoji = "🎯", Title = "Plugin Marketplace", Description = "Browse and install plugins easily." });
        Items.Add(new RoadmapItem { Emoji = "💾", Title = "Auto Save", Description = "Automatically save plugin configurations." });
    }

    // Example: dynamically add new items at runtime
    public void AddItem(RoadmapItem item) => Items.Add(item);


}