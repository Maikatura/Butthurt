using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butthurt.Contracts.Models;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Views;

public partial class RoadmapView : UserControl, IPageView
{
    public string Title => "Roadmap";
    public PackIconLucideKind Icon => PackIconLucideKind.Map;
    public SideBarType SidebarType => SideBarType.Footer;
    public event EventHandler? BackRequested;

    public RoadmapView()
    {
        InitializeComponent();

        AddRoadmapItem("Midnight support", "The addon works with World of Warcraft 12.0.", PackIconLucideKind.Swords, done: true);
        AddRoadmapItem("New interface", "A proper app instead of a console window.", PackIconLucideKind.Sparkles, done: true);
        AddRoadmapItem("Toy controls", "Connect, scan, test and mute toys from the app.", PackIconLucideKind.Vibrate, done: true);
        AddRoadmapItem("Addon settings in the app", "Pick events, intensity and duration without touching Lua.", PackIconLucideKind.Settings, done: true);
        AddRoadmapItem("Addon install button", "Copies the bundled addon into your WoW folder.", PackIconLucideKind.Download, done: true);
        AddRoadmapItem("User guide", "Setup steps and how to add new events.", PackIconLucideKind.BookOpen, done: true);
        AddRoadmapItem("Vibration patterns", "Pulses and waves instead of a single buzz.", PackIconLucideKind.Waves);
        AddRoadmapItem("Low health warning", "Keep buzzing while you are about to die.", PackIconLucideKind.HeartPulse);
        AddRoadmapItem("More games", "Plugins for other games that support addons.", PackIconLucideKind.Gamepad2);

        // Fewer columns when the window gets narrow.
        this.GetObservable(BoundsProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<Rect>(bounds =>
        {
            if (bounds.Width > 0) RoadmapContainer.Columns = bounds.Width < 820 ? 2 : 3;
        }));
    }

    /// <summary>
    /// Dynamically adds a roadmap card.
    /// </summary>
    public void AddRoadmapItem(string title, string subtitle, PackIconLucideKind icon, bool done = false)
    {
        var card = new Border
        {
            Classes = { "card" },
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                        Children =
                        {
                            new Border
                            {
                                Classes = { "iconbox" },
                                Child = new PackIconLucide
                                {
                                    Kind = icon, Width = 18, Height = 18,
                                    Foreground = new SolidColorBrush(Color.Parse("#D8B4FE")),
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                                }
                            },
                            Ui.WithColumn(new Border
                            {
                                Classes = { "pill" },
                                HorizontalAlignment = HorizontalAlignment.Right,
                                VerticalAlignment = VerticalAlignment.Top,
                                Child = new TextBlock
                                {
                                    Text = done ? "DONE" : "PLANNED",
                                    FontSize = 10,
                                    FontWeight = FontWeight.Bold,
                                    Foreground = done ? Ui.Brush("SuccessBrush") : Ui.Brush("MutedBrush")
                                }
                            }, 1)
                        }
                    },
                    new StackPanel
                    {
                        Spacing = 4,
                        Children = { Ui.Text(title, "h3"), Ui.Text(subtitle, "muted", 12) }
                    }
                }
            }
        };

        RoadmapContainer.Children.Add(card);
    }
}
