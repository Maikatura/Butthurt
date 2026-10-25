using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Services;
using Buttplug.Client;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Views;

public partial class ToysView : UserControl, IPageView
{
    public string Title => "Toys";
    public PackIconLucideKind Icon => PackIconLucideKind.Vibrate;
    public SideBarType SidebarType => SideBarType.Header;
    public event EventHandler? BackRequested;

    private bool _loading;

    public ToysView()
    {
        InitializeComponent();

        MasterSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            MasterText.Text = $"{MasterSlider.Value:0}%";
            if (_loading) return;
            SettingsService.Current.MasterIntensity = MasterSlider.Value / 100.0;
            SettingsService.SaveSoon();
        };

        UrlBox.LostFocus += (_, _) => SaveUrl();

        ButthurtService.StateChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);

        AttachedToVisualTree += (_, _) =>
        {
            _loading = true;
            UrlBox.Text = SettingsService.Current.IntifaceUrl;
            MasterSlider.Value = SettingsService.Current.MasterIntensity * 100;
            MasterText.Text = $"{MasterSlider.Value:0}%";
            _loading = false;
            Refresh();
        };
    }

    private void SaveUrl()
    {
        var url = UrlBox.Text?.Trim();
        if (string.IsNullOrEmpty(url) || url == SettingsService.Current.IntifaceUrl) return;

        SettingsService.Current.IntifaceUrl = url;
        SettingsService.Save();
    }

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ButthurtService.IsConnected)
        {
            await ButthurtService.Stop();
            return;
        }

        SaveUrl();
        if (!Uri.TryCreate(SettingsService.Current.IntifaceUrl, UriKind.Absolute, out _))
        {
            await NotificationService.ShowAsync("That server address doesn't look right, it should start with ws://");
            return;
        }

        if (!await ButthurtService.Start())
        {
            await NotificationService.ShowAsync("Could not reach Intiface Central. Is the server started?");
        }
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        await ButthurtService.ScanForDevices();
    }

    private void Refresh()
    {
        var connected = ButthurtService.IsConnected;

        ConnectButton.Content = connected ? "Disconnect" : ButthurtService.IsConnecting ? "Connecting…" : "Connect";
        ConnectButton.IsEnabled = !ButthurtService.IsConnecting;
        ConnectButton.Classes.Set("primary", !connected);
        ConnectButton.Classes.Set("danger", connected);
        UrlBox.IsEnabled = !connected;

        ConnectionText.Text = connected ? $"Connected to {SettingsService.Current.IntifaceUrl}"
            : ButthurtService.LastError is { } error ? $"Could not connect: {error}"
            : "Not connected";

        ScanButton.IsEnabled = connected && !ButthurtService.IsScanning;
        ScanText.Text = ButthurtService.IsScanning ? "Scanning…" : "Scan for toys";

        var devices = ButthurtService.Devices;
        DeviceList.Children.Clear();
        foreach (var device in devices)
        {
            DeviceList.Children.Add(CreateDeviceRow(device));
        }

        EmptyState.IsVisible = devices.Count == 0;
        EmptyStateText.Text = !connected ? "Connect to Intiface Central to see your toys."
            : ButthurtService.IsScanning ? "Looking for toys… make sure yours is turned on."
            : "No toys found. Turn your toy on and press Scan.";
    }

    private Control CreateDeviceRow(ButtplugClientDevice device)
    {
        var canVibrate = ButthurtService.CanVibrate(device);

        var toggle = new ToggleSwitch
        {
            IsChecked = ButthurtService.IsDeviceEnabled(device.Name),
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = canVibrate
        };
        toggle.IsCheckedChanged += (_, _) => ButthurtService.SetDeviceEnabled(device.Name, toggle.IsChecked == true);

        var testButton = new Button
        {
            Classes = { "bt", "small" },
            Content = "Test",
            IsEnabled = canVibrate,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        testButton.Click += async (_, _) => await ButthurtService.VibrateDevice(device, 0.5, 700);

        return new Border
        {
            Classes = { "row" },
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                Children =
                {
                    new Border
                    {
                        Classes = { "iconbox" },
                        Margin = new Thickness(0, 0, 14, 0),
                        Child = new PackIconLucide
                        {
                            Kind = PackIconLucideKind.Vibrate, Width = 18, Height = 18,
                            Foreground = new SolidColorBrush(Color.Parse("#D8B4FE")),
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    Ui.WithColumn(new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Spacing = 2,
                        Children =
                        {
                            Ui.Text(device.Name, "h3"),
                            Ui.Text(canVibrate
                                ? $"{device.VibrateAttributes.Count} vibrator{(device.VibrateAttributes.Count == 1 ? "" : "s")}"
                                : "This toy can't vibrate, so Butthurt can't use it yet", "muted", 12)
                        }
                    }, 1),
                    Ui.WithColumn(toggle, 2),
                    Ui.WithColumn(testButton, 3)
                }
            }
        };
    }
}
