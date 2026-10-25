using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Views;

public partial class WowView : UserControl, IPageView
{
    public string Title => "World of Warcraft";
    public PackIconLucideKind Icon => PackIconLucideKind.Swords;
    public SideBarType SidebarType => SideBarType.Plugins;
    public event EventHandler? BackRequested;

    public WowView()
    {
        InitializeComponent();

        WowPathBox.LostFocus += (_, _) => SetWowPath(WowPathBox.Text);
        WowWatcherService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateWatcher);

        AttachedToVisualTree += (_, _) =>
        {
            if (string.IsNullOrEmpty(SettingsService.Current.WowPath))
            {
                // First visit, try to save the user a trip through the folder picker.
                var detected = AddonInstaller.DetectWowPath();
                if (detected != null)
                {
                    SettingsService.Current.WowPath = detected;
                    SettingsService.Save();
                }
            }

            WowPathBox.Text = SettingsService.Current.WowPath;
            BuildEventList();
            RefreshFlavors();
            UpdateWatcher();
        };
    }

    private void WatcherButton_Click(object? sender, RoutedEventArgs e)
    {
        if (WowWatcherService.IsRunning) WowWatcherService.Stop();
        else WowWatcherService.Start();
    }

    private void UpdateWatcher()
    {
        var running = WowWatcherService.IsRunning;
        WatcherButton.Content = running ? "Stop" : "Start";
        WatcherButton.Classes.Set("primary", !running);
        WatcherButton.Classes.Set("danger", running);

        var (text, brushKey) = !running ? ("STOPPED", "DimBrush")
            : WowWatcherService.IsWowFocused ? ("WATCHING", "SuccessBrush")
            : ("WAITING FOR WOW", "WarningBrush");

        WatcherPillText.Text = text;
        WatcherPillText.Foreground = Ui.Brush(brushKey);
    }

    #region Events

    private void BuildEventList()
    {
        EventList.Children.Clear();
        foreach (var definition in WowEvents.All)
        {
            EventList.Children.Add(CreateEventRow(definition));
        }
    }

    private Control CreateEventRow(WowEventDefinition definition)
    {
        var settings = SettingsService.GetEvent(definition.Type);
        var color = Color.FromRgb(definition.R, definition.G, definition.B);

        var intensityLabel = Ui.Text($"{settings.Intensity * 100:0}%", "muted", 12);
        var intensity = new Slider { Minimum = 5, Maximum = 100, Value = settings.Intensity * 100 };
        intensity.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            settings.Intensity = intensity.Value / 100.0;
            intensityLabel.Text = $"{intensity.Value:0}%";
            SettingsService.SaveSoon();
        };

        var durationLabel = Ui.Text(FormatDuration(settings.DurationMs), "muted", 12);
        var duration = new Slider { Minimum = 100, Maximum = 5000, Value = settings.DurationMs };
        duration.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            settings.DurationMs = (int)(Math.Round(duration.Value / 50) * 50);
            durationLabel.Text = FormatDuration(settings.DurationMs);
            SettingsService.SaveSoon();
        };

        var sliders = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Margin = new Thickness(54, 4, 0, 0),
            Opacity = settings.Enabled ? 1 : 0.4,
            Children =
            {
                SliderBlock("Intensity", intensityLabel, intensity),
                Ui.WithColumn(SliderBlock("Duration", durationLabel, duration), 2)
            }
        };

        var toggle = new ToggleSwitch
        {
            IsChecked = settings.Enabled,
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            settings.Enabled = toggle.IsChecked == true;
            sliders.Opacity = settings.Enabled ? 1 : 0.4;
            SettingsService.Save();
        };

        var testButton = new Button
        {
            Classes = { "bt", "small" },
            Content = "Test",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        testButton.Click += async (_, _) =>
        {
            if (!ButthurtService.IsConnected)
            {
                await NotificationService.ShowAsync("Connect to Intiface Central to feel the test.");
            }

            WowWatcherService.Simulate(definition.Type);
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Children =
            {
                new Border
                {
                    Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 14, 0),
                    Background = new SolidColorBrush(color, 0.15),
                    BorderBrush = new SolidColorBrush(color, 0.5),
                    BorderThickness = new Thickness(1),
                    Child = new PackIconLucide
                    {
                        Kind = definition.Icon, Width = 18, Height = 18,
                        Foreground = new SolidColorBrush(Lighten(color)),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                },
                Ui.WithColumn(new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2,
                    Children =
                    {
                        Ui.Text(definition.Name, "h3"),
                        Ui.Text(definition.Description, "muted", 12)
                    }
                }, 1),
                Ui.WithColumn(toggle, 2),
                Ui.WithColumn(testButton, 3)
            }
        };

        return new Border
        {
            Classes = { "row" },
            Child = new StackPanel { Spacing = 6, Children = { header, sliders } }
        };
    }

    private static Control SliderBlock(string title, TextBlock valueLabel, Slider slider)
    {
        Grid.SetColumn(valueLabel, 1);
        return new StackPanel
        {
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children = { Ui.Text(title, "muted", 12), valueLabel }
                },
                slider
            }
        };
    }

    private static string FormatDuration(int ms) => ms < 1000 ? $"{ms} ms" : $"{ms / 1000.0:0.##} s";

    // Pure blue/red icons are hard to read on a dark background.
    private static Color Lighten(Color c) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * 0.45), (byte)(c.G + (255 - c.G) * 0.45), (byte)(c.B + (255 - c.B) * 0.45));

    #endregion

    #region Addon

    private void SetWowPath(string? path)
    {
        var normalized = AddonInstaller.NormalizeWowPath(path);
        if (normalized == SettingsService.Current.WowPath)
        {
            RefreshFlavors();
            return;
        }

        SettingsService.Current.WowPath = normalized ?? path?.Trim();
        SettingsService.Save();
        WowPathBox.Text = SettingsService.Current.WowPath;
        RefreshFlavors();
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select your World of Warcraft folder",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) SetWowPath(path);
    }

    private async void DetectButton_Click(object? sender, RoutedEventArgs e)
    {
        var detected = AddonInstaller.DetectWowPath();
        if (detected == null)
        {
            await NotificationService.ShowAsync("Couldn't find World of Warcraft, please pick the folder with Browse.");
            return;
        }

        SetWowPath(detected);
        await NotificationService.ShowAsync($"Found World of Warcraft in {detected}");
    }

    private async void AutoInstallButton_Click(object? sender, RoutedEventArgs e)
    {
        AutoInstallButton.IsEnabled = false;
        await Ui.AutoInstallAddon();
        AutoInstallButton.IsEnabled = true;

        WowPathBox.Text = SettingsService.Current.WowPath;
        RefreshFlavors();
    }

    private void RefreshFlavors()
    {
        FlavorList.Children.Clear();

        var wowPath = SettingsService.Current.WowPath;
        if (string.IsNullOrWhiteSpace(wowPath) || !Directory.Exists(wowPath))
        {
            FlavorHint.Text = "Pick the folder that contains _retail_ (or _classic_), usually C:\\Program Files (x86)\\World of Warcraft.";
            FlavorHint.IsVisible = true;
            return;
        }

        var flavors = AddonInstaller.GetFlavors(wowPath).ToList();
        FlavorHint.IsVisible = flavors.Count == 0;
        FlavorHint.Text = "No game versions found in this folder. It should contain _retail_ or _classic_.";

        foreach (var flavor in flavors)
        {
            FlavorList.Children.Add(CreateFlavorRow(wowPath, flavor));
        }
    }

    private Control CreateFlavorRow(string wowPath, WowFlavor flavor)
    {
        var installed = AddonInstaller.GetInstalledVersion(wowPath, flavor);
        var isInstalled = AddonInstaller.IsInstalled(wowPath, flavor);
        var bundled = AddonInstaller.GetBundledVersion(flavor);
        var upToDate = isInstalled && installed == bundled;

        var status = !isInstalled ? "Not installed"
            : upToDate ? $"Installed · {installed}"
            : $"Installed · {installed ?? "unknown"} → {bundled ?? "unknown"} available";

        var button = new Button
        {
            Classes = { "bt", "small" },
            Content = !isInstalled ? "Install" : upToDate ? "Reinstall" : "Update",
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = bundled != null
        };
        if (!upToDate) button.Classes.Add("primary");

        button.Click += async (_, _) =>
        {
            try
            {
                AddonInstaller.Install(wowPath, flavor);
                await NotificationService.ShowAsync($"Addon installed for {flavor.DisplayName}. Type /reload in game.");
            }
            catch (UnauthorizedAccessException)
            {
                await NotificationService.ShowAsync("Windows blocked the install. Try running Butthurt as administrator.", 5000);
            }
            catch (Exception ex)
            {
                ActivityLog.Add($"Addon install failed: {ex.Message}", LogLevel.Error);
                await NotificationService.ShowAsync($"Install failed: {ex.Message}", 5000);
            }

            RefreshFlavors();
        };

        return new Border
        {
            Classes = { "row" },
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new PackIconLucide
                    {
                        Kind = upToDate ? PackIconLucideKind.CircleCheck : PackIconLucideKind.Download,
                        Width = 18, Height = 18,
                        Foreground = upToDate ? Ui.Brush("SuccessBrush") : Ui.Brush("MutedBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 14, 0)
                    },
                    Ui.WithColumn(new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Spacing = 2,
                        Children =
                        {
                            Ui.Text(flavor.DisplayName, "h3"),
                            Ui.Text(status, "muted", 12)
                        }
                    }, 1),
                    Ui.WithColumn(button, 2)
                }
            }
        };
    }

    #endregion
}
