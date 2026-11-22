using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Views;

public partial class HomeView : UserControl, IPageView
{
    private const int MaxActivityRows = 9;

    public string Title => "Home";
    public PackIconLucideKind Icon => PackIconLucideKind.House;
    public SideBarType SidebarType => SideBarType.Header;
    public event EventHandler? BackRequested;

    // Headline and detail text of every plugin card, so status updates don't rebuild the cards.
    private readonly Dictionary<IPlugin, (TextBlock Headline, TextBlock Detail)> _pluginCards = new();

    public HomeView()
    {
        InitializeComponent();

        ButthurtService.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        PluginManager.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        PluginManager.PluginsChanged += (_, _) =>
        {
            BuildPluginCards();
            UpdateStatus();
        };
        ActivityLog.EntryAdded += (_, _) => Dispatcher.UIThread.Post(UpdateActivity);

        StatusGrid.GetObservable(BoundsProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<Rect>(_ => UpdateColumns()));

        AttachedToVisualTree += (_, _) =>
        {
            BuildPluginCards();
            UpdateStatus();
            UpdateActivity();
        };
    }

    // With the Toys plugin on, the toy connection is what makes things buzz (the other plugins are cheap and usually
    // already running). Otherwise "running" means any plugin is doing something.
    private static bool ToysPluginOn => PluginManager.Plugins.Any(p => p.Id == "Toys");

    private bool IsRunning => ToysPluginOn
        ? ButthurtService.IsConnected
        : PluginManager.Plugins.Any(p => p.GetStatus().Level != PluginStatusLevel.Off);

    private async void StartStopButton_Click(object? sender, RoutedEventArgs e)
    {
        StartStopButton.IsEnabled = false;
        try
        {
            if (!IsRunning)
            {
                // The Toys plugin connects to Intiface (and says so when it can't).
                await PluginManager.StartAll();
            }
            else
            {
                await PluginManager.StopAll();
            }
        }
        finally
        {
            StartStopButton.IsEnabled = true;
            UpdateStatus();
        }
    }

    private void BuildPluginCards()
    {
        // Plugins can be turned on and off, so start over.
        StatusGrid.Children.Clear();
        _pluginCards.Clear();

        foreach (var plugin in PluginManager.Plugins)
        {
            var headline = Ui.Text("", "stat");
            var detail = Ui.Text("", "muted", 12);

            var open = new Button { Classes = { "bt", "ghost", "small" }, Content = "Open", HorizontalAlignment = HorizontalAlignment.Right };
            open.Click += (_, _) => NavigationService.NavigateToPlugin(plugin.Id);

            var content = new StackPanel
            {
                Spacing = 14,
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
                                    Kind = plugin.Icon, Width = 20, Height = 20,
                                    Foreground = new SolidColorBrush(Color.Parse("#FAFAFA")),
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                                }
                            },
                            Ui.WithColumn(open, 1)
                        }
                    },
                    new StackPanel
                    {
                        Spacing = 2,
                        Children = { Ui.Text(plugin.Name.ToUpperInvariant(), "caption"), headline, detail }
                    }
                }
            };

            if (plugin.HomeAction is { } action)
            {
                var button = new Button
                {
                    Classes = { "bt", "small" },
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new PackIconLucide { Kind = action.Icon, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center }
                        }
                    }
                };
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    await action.Run();
                    button.IsEnabled = true;
                };
                content.Children.Add(button);
            }

            StatusGrid.Children.Add(new Border { Classes = { "card" }, Margin = new Thickness(0, 0, 12, 12), Child = content });
            _pluginCards[plugin] = (headline, detail);
        }

        UpdateColumns();
    }

    // Two columns on narrow windows, otherwise everything on one row (up to four).
    private void UpdateColumns()
    {
        var cards = StatusGrid.Children.Count(c => c.IsVisible);
        StatusGrid.Columns = StatusGrid.Bounds.Width is > 0 and < 1000 ? Math.Min(2, Math.Max(cards, 1)) : Math.Clamp(cards, 1, 4);
    }

    /// <summary>Talks about toys or about streaming, depending on whether the Toys plugin is on.</summary>
    private void ApplyToyMode()
    {
        var toys = PluginManager.ToysEnabled;

        HeroTitle.Text = toys ? "Get physically notified." : "Let your stream play along.";
        HeroText.Text = toys
            ? "Connect your toys or PiShock and feel every hit you take in game, and every cheer, sub, tip and redeem on stream."
            : "Bits, subs and redeems on Twitch change your VRChat avatar, size and parameters, all through rules you control.";
        EmptyActivityText.Text = toys
            ? "Nothing yet. Events from the game and your toys will show up here."
            : "Nothing yet. Events from Twitch and VRChat will show up here.";

        UpdateColumns();
    }

    private void UpdateStatus()
    {
        ApplyToyMode();

        var running = IsRunning;
        StartStopText.Text = running ? "Stop" : "Start";
        StartStopIcon.Kind = running ? PackIconLucideKind.Square : PackIconLucideKind.Play;
        StartStopButton.Classes.Set("primary", !running);
        StartStopButton.Classes.Set("danger", running);

        foreach (var (plugin, (headline, detail)) in _pluginCards)
        {
            var status = plugin.GetStatus();
            headline.Text = status.Headline;
            detail.Text = status.Detail;
        }
    }

    private void UpdateActivity()
    {
        var entries = ActivityLog.Entries.Reverse().Take(MaxActivityRows).ToList();

        ActivityList.Children.Clear();
        foreach (var entry in entries)
        {
            ActivityList.Children.Add(Ui.ActivityRow(entry));
        }

        EmptyActivityText.IsVisible = entries.Count == 0;
    }
}
