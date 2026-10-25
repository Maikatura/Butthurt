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

        TestIntensitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) TestIntensityText.Text = $"{TestIntensitySlider.Value:0}%";
        };

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

    // The toy connection is what makes things buzz, the plugins are cheap and usually already running.
    // Without toys, "running" means any plugin is doing something.
    private bool IsRunning => PluginManager.ToysEnabled
        ? ButthurtService.IsConnected
        : PluginManager.Plugins.Any(p => p.GetStatus().Level != PluginStatusLevel.Off);

    private async void StartStopButton_Click(object? sender, RoutedEventArgs e)
    {
        StartStopButton.IsEnabled = false;
        try
        {
            if (!IsRunning)
            {
                await PluginManager.StartAll();
                if (PluginManager.ToysEnabled && !await ButthurtService.Start())
                {
                    await NotificationService.ShowAsync("Could not reach Intiface Central. Is the server started?");
                }
            }
            else
            {
                await PluginManager.StopAll();
                await ButthurtService.Stop();
            }
        }
        finally
        {
            StartStopButton.IsEnabled = true;
            UpdateStatus();
        }
    }

    private async void TestButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!ButthurtService.IsConnected)
        {
            await NotificationService.ShowAsync("Connect to Intiface Central first.");
            return;
        }

        if (ButthurtService.Devices.Count == 0)
        {
            await NotificationService.ShowAsync("No toys connected yet, try scanning on the Toys page.");
            return;
        }

        ActivityLog.Add($"Test buzz at {TestIntensitySlider.Value:0}%", LogLevel.Event);
        await ButthurtService.Vibrate(TestIntensitySlider.Value / 100.0, 1000);
    }

    private void ManageToys_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateTo<ToysView>();

    private void BuildPluginCards()
    {
        // Plugins can be turned on and off, so start over (the first two cards are Intiface and toys).
        while (StatusGrid.Children.Count > 2) StatusGrid.Children.RemoveAt(StatusGrid.Children.Count - 1);
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
                                    Foreground = new SolidColorBrush(Color.Parse("#D8B4FE")),
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

    /// <summary>Shows or hides everything about toys, see AppSettings.ToysEnabled.</summary>
    private void ApplyToyMode()
    {
        var toys = PluginManager.ToysEnabled;

        IntifaceCard.IsVisible = toys;
        ToysCard.IsVisible = toys;
        QuickTestCard.IsVisible = toys;

        // Without the quick test card, recent activity gets the whole row.
        Grid.SetColumn(ActivityCard, toys ? 2 : 0);
        Grid.SetColumnSpan(ActivityCard, toys ? 1 : 3);

        HeroTitle.Text = toys ? "Get physically notified." : "Let your stream play along.";
        HeroText.Text = toys
            ? "Connect your toy through Intiface Central and feel every hit you take in game, and every cheer, sub and redeem on stream."
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

        if (ButthurtService.IsConnected)
        {
            IntifaceText.Text = "Connected";
            IntifaceSubText.Text = SettingsService.Current.IntifaceUrl;
        }
        else if (ButthurtService.IsConnecting)
        {
            IntifaceText.Text = "Connecting…";
            IntifaceSubText.Text = SettingsService.Current.IntifaceUrl;
        }
        else
        {
            IntifaceText.Text = "Offline";
            IntifaceSubText.Text = ButthurtService.LastError ?? "Press Start to connect";
        }

        var devices = ButthurtService.Devices;
        var enabled = devices.Count(d => ButthurtService.IsDeviceEnabled(d.Name));
        ToysText.Text = devices.Count == 1 ? "1 connected" : $"{devices.Count} connected";
        ToysSubText.Text = ButthurtService.IsScanning ? "Scanning…"
            : devices.Count == 0 ? "Turn on your toy and scan"
            : $"{enabled} enabled for haptics";

        foreach (var (plugin, (headline, detail)) in _pluginCards)
        {
            var status = plugin.GetStatus();
            headline.Text = status.Headline;
            detail.Text = status.Detail;
        }

        TestButton.IsEnabled = ButthurtService.IsConnected;
        TestHintText.Text = ButthurtService.IsConnected
            ? "Every enabled toy buzzes, scaled by your master intensity."
            : "Connect to Intiface Central to test.";
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
