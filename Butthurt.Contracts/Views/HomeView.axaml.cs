using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Butthurt.Contracts.Models;
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

    public HomeView()
    {
        InitializeComponent();

        TestIntensitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) TestIntensityText.Text = $"{TestIntensitySlider.Value:0}%";
        };

        ButthurtService.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        WowWatcherService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        WowWatcherService.EventDetected += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        ActivityLog.EntryAdded += (_, _) => Dispatcher.UIThread.Post(UpdateActivity);

        AttachedToVisualTree += (_, _) =>
        {
            UpdateStatus();
            UpdateActivity();
        };
    }

    // The toy connection is what makes things buzz, the watcher is cheap and usually already running.
    private bool IsRunning => ButthurtService.IsConnected;

    private async void StartStopButton_Click(object? sender, RoutedEventArgs e)
    {
        StartStopButton.IsEnabled = false;
        try
        {
            if (!IsRunning)
            {
                WowWatcherService.Start();
                if (!await ButthurtService.Start())
                {
                    await NotificationService.ShowAsync("Could not reach Intiface Central. Is the server started?");
                }
            }
            else
            {
                WowWatcherService.Stop();
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

    private async void AutoInstallButton_Click(object? sender, RoutedEventArgs e)
    {
        AutoInstallButton.IsEnabled = false;
        await Ui.AutoInstallAddon();
        AutoInstallButton.IsEnabled = true;
    }

    private void ManageToys_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateTo<ToysView>();

    private void ManageWow_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateTo<WowView>();

    private void UpdateStatus()
    {
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

        if (!WowWatcherService.IsRunning)
        {
            WowText.Text = "Stopped";
            WowSubText.Text = "The watcher is not running";
        }
        else
        {
            WowText.Text = WowWatcherService.IsWowFocused ? "Watching" : "Waiting";
            WowSubText.Text = WowWatcherService.LastEvent is { } last && WowWatcherService.LastEventTime is { } time
                ? $"Last: {last.Name} at {time:HH:mm:ss}"
                : WowWatcherService.IsWowFocused ? "Ready for events" : "Switch to the game window";
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
