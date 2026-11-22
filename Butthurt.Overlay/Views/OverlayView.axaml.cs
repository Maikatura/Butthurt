using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Overlay.Views;

public partial class OverlayView : UserControl, IPageView
{
    public string Title => "Stream overlay";
    public PackIconLucideKind Icon => PackIconLucideKind.MonitorPlay;
    public SideBarType SidebarType => SideBarType.Streaming;
    public event EventHandler? BackRequested;

    private bool _loading;

    private static OverlaySettings Settings => SettingsService.Current.Overlay;

    public OverlayView()
    {
        InitializeComponent();

        void Changed()
        {
            if (_loading) return;
            Settings.ShowQueue = QueueCheck.IsChecked == true;
            Settings.ShowAlerts = AlertsCheck.IsChecked == true;
            Settings.AlertSeconds = (int)(AlertSecondsBox.Value ?? 6);
            Settings.Port = (int)(PortBox.Value ?? 7878);
            SettingsService.Save();
            OverlayService.ApplySettings();
            Refresh();
        }

        QueueCheck.IsCheckedChanged += (_, _) => Changed();
        AlertsCheck.IsCheckedChanged += (_, _) => Changed();
        AlertSecondsBox.ValueChanged += (_, _) => Changed();
        PortBox.LostFocus += (_, _) => Changed();
        OverlayService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);

        ScaleSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var scale = Math.Round(ScaleSlider.Value * 10) / 10;
            ScaleText.Text = $"{scale * 100:0}%";
            if (_loading) return;
            Settings.Scale = scale;
            SettingsService.SaveSoon();
            OverlayService.ApplySettings();
        };

        AttachedToVisualTree += (_, _) =>
        {
            _loading = true;
            QueueCheck.IsChecked = Settings.ShowQueue;
            AlertsCheck.IsChecked = Settings.ShowAlerts;
            AlertSecondsBox.Value = Settings.AlertSeconds;
            PortBox.Value = Settings.Port;
            ScaleSlider.Value = Settings.Scale;
            ScaleText.Text = $"{Settings.Scale * 100:0}%";
            BuildPositionPickers();
            _loading = false;
            Refresh();
        };
    }

    private void Refresh()
    {
        UrlBox.Text = OverlayService.Url;

        var running = OverlayService.IsRunning;
        var clients = OverlayService.ConnectedClients;
        var problem = !running && OverlayService.LastError != null;
        var (pill, brushKey) = problem ? ("PROBLEM", "DangerBrush")
            : !running ? ("STOPPED", "DimBrush")
            : clients > 0 ? (clients == 1 ? "1 SOURCE CONNECTED" : $"{clients} SOURCES CONNECTED", "SuccessBrush")
            : ("WAITING FOR OBS", "WarningBrush");
        StatePillText.Text = pill;
        StatePillText.Foreground = Ui.Brush(brushKey);

        StateText.Text = problem ? OverlayService.LastError
            : !running ? "Press Start to serve the overlay."
            : clients > 0 ? "OBS is showing the overlay."
            : "Running. Add the address below as a Browser source in OBS.";

        StartStopButton.Content = running ? "Stop" : "Start";
        StartStopButton.Classes.Set("primary", !running);
        StartStopButton.Classes.Set("danger", running);
    }

    private void StartStop_Click(object? sender, RoutedEventArgs e)
    {
        if (OverlayService.IsRunning) OverlayService.Stop();
        else OverlayService.Start();
    }

    private void BuildPositionPickers()
    {
        AlertPositionHost.Content = PositionPicker(Settings.AlertPosition, Enum.GetValues<OverlayPosition>(), position =>
        {
            Settings.AlertPosition = position;
            SettingsService.Save();
            OverlayService.ApplySettings();
            BuildPositionPickers();
        });

        // The queue is a tall list, it only makes sense in a corner.
        QueuePositionHost.Content = PositionPicker(Settings.QueuePosition,
            new[] { OverlayPosition.TopLeft, OverlayPosition.TopRight, OverlayPosition.BottomLeft, OverlayPosition.BottomRight }, position =>
            {
                Settings.QueuePosition = position;
                SettingsService.Save();
                OverlayService.ApplySettings();
                BuildPositionPickers();
            });
    }

    /// <summary>A little 16:9 "screen" with a button for each spot. The chosen one is filled in.</summary>
    private static Control PositionPicker(OverlayPosition current, IReadOnlyCollection<OverlayPosition> allowed, Action<OverlayPosition> picked)
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*,*"),
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            Width = 144, Height = 81
        };

        foreach (var position in Enum.GetValues<OverlayPosition>())
        {
            var index = (int)position;
            var selected = position == current;

            var spot = new Button
            {
                Classes = { "bt" },
                Padding = new Thickness(0),
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsVisible = allowed.Contains(position),
                Background = selected ? Ui.Brush("AccentGradient") : new SolidColorBrush(Color.Parse("#18181B")),
                BorderBrush = selected ? Brushes.Transparent : Ui.Brush("BorderBrush")
            };
            ToolTip.SetTip(spot, Regex.Replace(position.ToString(), "(?<!^)([A-Z])", " $1"));
            Avalonia.Automation.AutomationProperties.SetName(spot, position.ToString());
            spot.Click += (_, _) => picked(position);

            Grid.SetRow(spot, index / 3);
            Grid.SetColumn(spot, index % 3);
            grid.Children.Add(spot);
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0B0B10")),
            BorderBrush = Ui.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = grid
        };
    }

    private async void CopyUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(OverlayService.Url);
            await NotificationService.ShowAsync("Overlay address copied. Paste it into an OBS Browser source.");
        }
    }

    private async void Preview_Click(object? sender, RoutedEventArgs e)
    {
        if (!OverlayService.IsRunning)
        {
            await NotificationService.ShowAsync("Start the overlay first.");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OverlayService.Url) { UseShellExecute = true });
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!System.IO.Directory.Exists(OverlayService.Folder))
        {
            await NotificationService.ShowAsync("Start the overlay once to create its folder.");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OverlayService.Folder) { UseShellExecute = true });
        await NotificationService.ShowAsync("Careful: changing the overlay files can break the overlay. Read README.txt first.");
    }

    private async void TestAnnouncement_Click(object? sender, RoutedEventArgs e) => await ShowTestAnnouncement();

    /// <summary>Shows a sample announcement on the overlay, for the page's button and the home card.</summary>
    public static async Task<bool> ShowTestAnnouncement()
    {
        if (!OverlayService.IsRunning)
        {
            await NotificationService.ShowAsync("Start the overlay first.");
            return false;
        }

        if (!Settings.ShowAlerts)
        {
            await NotificationService.ShowAsync("Announcements are turned off on the Stream overlay page.");
            return false;
        }

        OverlayService.Alert("TestUser cheered 50 bits: ×1.1 for 30s!");
        return true;
    }
}
