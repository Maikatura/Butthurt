using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using Butthurt.Fansly.Models;
using Butthurt.Fansly.Services;
using Butthurt.Fansly.Views;
using Butthurt.PiShock.Models;
using Butthurt.PiShock.Services;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.Twitch.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.PiShock.Views;

public partial class PiShockView : UserControl, IPageView
{
    public string Title => "PiShock";
    public PackIconLucideKind Icon => PackIconLucideKind.Zap;
    public SideBarType SidebarType => SideBarType.Hardware;
    public event EventHandler? BackRequested;

    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FBBF24"));
    private static readonly IBrush AmberSoft = new SolidColorBrush(Color.Parse("#26F59E0B"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush RedSoft = new SolidColorBrush(Color.Parse("#26EF4444"));

    private bool _loading;

    private static PiShockSettings Settings => PiShockPlugin.Settings;

    public PiShockView()
    {
        InitializeComponent();

        SenderNameBox.LostFocus += (_, _) =>
        {
            var value = SenderNameBox.Text?.Trim() ?? "";
            if (value == Settings.SenderName) return;
            Settings.SenderName = value.Length == 0 ? "Butthurt" : value;
            SettingsService.Save();
        };

        ShocksAllowedToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Settings.ShocksAllowed = ShocksAllowedToggle.IsChecked == true;
            SettingsService.Save();
            ActivityLog.Add(Settings.ShocksAllowed ? "PiShock: shocks allowed" : "PiShock: shocks turned off", LogLevel.Warning);
            PiShockService.NotifyStatusChanged();
        };

        PausedToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Settings.Paused = PausedToggle.IsChecked == true;
            if (Settings.Paused) PiShockService.StopAll();
            SettingsService.Save();
            ActivityLog.Add(Settings.Paused ? "PiShock paused" : "PiShock unpaused", LogLevel.Warning);
            PiShockService.NotifyStatusChanged();
        };

        PiShockService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateAccount);

        AttachedToVisualTree += (_, _) =>
        {
            _loading = true;
            SenderNameBox.Text = Settings.SenderName;
            ShocksAllowedToggle.IsChecked = Settings.ShocksAllowed;
            PausedToggle.IsChecked = Settings.Paused;
            _loading = false;

            UpdateAccount();
            BuildShockerList();
        };
    }

    #region Account

    private void OpenTwitch_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateToPlugin(Twitch.TwitchPlugin.PluginId);

    private void OpenFansly_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateToPlugin(Fansly.FanslyPlugin.PluginId);

    private void OpenWow_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateToPlugin(Warcraft.WarcraftPlugin.PluginId);

    private void OpenVRChat_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateToPlugin(VRChat.VRChatPlugin.PluginId);

    private void OpenAccount_Click(object? sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://login.pishock.com/account") { UseShellExecute = true });

    private async void SaveApiKey_Click(object? sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Text?.Trim() ?? "";
        if (key.Length == 0) return;

        PiShockService.SetApiKey(key);
        ApiKeyBox.Text = "";

        // Check it right away, so a wrong key shows up here and not on stream.
        SaveApiKeyButton.IsEnabled = false;
        var error = await PiShockService.CheckLogin();
        SaveApiKeyButton.IsEnabled = true;
        UpdateAccount();

        await NotificationService.ShowAsync(error != null
            ? $"API key saved, but {error}"
            : $"Logged in as {Settings.Username}. Press Find my shockers to add your shockers.", error != null ? 8000 : 4000);
    }

    private void UpdateAccount()
    {
        var (pill, brushKey) = !PiShockService.HasLogin ? ("NOT SET UP", "DimBrush")
            : Settings.Paused ? ("PAUSED", "WarningBrush")
            : !PiShockService.HasShockers ? ("NO SHOCKERS", "WarningBrush")
            : PiShockService.LastError != null ? ("PROBLEM", "DangerBrush")
            : ("READY", "SuccessBrush");
        StatePillText.Text = pill;
        StatePillText.Foreground = Ui.Brush(brushKey);

        AccountText.Text = !PiShockService.HasLogin ? "Add your PiShock API key."
            : PiShockService.LastError ?? (PiShockService.LastAction is { } last
                ? $"Last: {last} at {PiShockService.LastActionTime:HH:mm:ss}"
                : Settings.Username.Length > 0 ? $"Logged in as {Settings.Username}" : "API key saved");

        ApiKeyBox.Watermark = string.IsNullOrEmpty(Settings.ApiKey) ? "API key" : "A key is saved. Paste a new one to replace it";

        var sources = new List<string>();
        if (PiShockPlugin.IsTwitchPluginOn) sources.Add("Twitch");
        if (PiShockPlugin.IsFanslyPluginOn) sources.Add("Fansly");
        if (PiShockPlugin.IsWarcraftPluginOn) sources.Add("WoW");
        if (PiShockPlugin.IsVRChatPluginOn) sources.Add("VRChat");
        RulesHint.Text = sources.Count == 0
            ? "What makes your shocker go off is set on the Twitch, Fansly, WoW and VRChat pages. Turn one of them on in Settings first."
            : $"What makes your shocker go off is set on the {string.Join(", ", sources)} page{(sources.Count == 1 ? "" : "s")}, " +
              "under PiShock rules. This page is only the setup, and its limits win over every rule.";
        OpenTwitchButton.IsVisible = PiShockPlugin.IsTwitchPluginOn;
        OpenFanslyButton.IsVisible = PiShockPlugin.IsFanslyPluginOn;
        OpenWowButton.IsVisible = PiShockPlugin.IsWarcraftPluginOn;
        OpenVRChatButton.IsVisible = PiShockPlugin.IsVRChatPluginOn;
    }

    #endregion

    #region Shockers

    private async void AddShocker_Click(object? sender, RoutedEventArgs e)
    {
        var code = NewShockerCode.Text?.Trim() ?? "";
        if (code.Length == 0)
        {
            await NotificationService.ShowAsync("Paste a share code, or press Find my shockers for your own.");
            return;
        }

        var name = NewShockerName.Text?.Trim() ?? "";
        Settings.Shockers.Add(new PiShockShocker
        {
            Name = name.Length > 0 ? name : $"Shocker {Settings.Shockers.Count + 1}",
            ShareCode = code
        });
        SettingsService.Save();

        NewShockerName.Text = "";
        NewShockerCode.Text = "";
        BuildShockerList();
        UpdateAccount();
        PiShockService.NotifyStatusChanged();
    }

    private async void FindShockers_Click(object? sender, RoutedEventArgs e)
    {
        FindShockersButton.IsEnabled = false;
        var (added, error) = await PiShockService.FindMyShockers();
        FindShockersButton.IsEnabled = true;

        if (error != null)
        {
            await NotificationService.ShowAsync(error, 6000);
            return;
        }

        BuildShockerList();
        UpdateAccount();
        await NotificationService.ShowAsync(added == 0
            ? "All your shockers are already in the list."
            : $"Added {added} shocker{(added == 1 ? "" : "s")}. Set their limits, then try Beep.");
    }

    private void BuildShockerList()
    {
        ShockerList.Children.Clear();
        foreach (var shocker in Settings.Shockers) ShockerList.Children.Add(CreateShockerRow(shocker));
        NoShockersText.IsVisible = Settings.Shockers.Count == 0;
    }

    private Control CreateShockerRow(PiShockShocker shocker)
    {
        var nameBox = new TextBox { Classes = { "field" }, Text = shocker.Name, Watermark = "Name" };
        nameBox.LostFocus += (_, _) =>
        {
            shocker.Name = nameBox.Text?.Trim() is { Length: > 0 } n ? n : "Shocker";
            SettingsService.Save();
        };

        var codeBox = new TextBox { Classes = { "field" }, Text = shocker.ShareCode, Watermark = "Share code (optional)", PasswordChar = '•' };
        void CommitCode()
        {
            var code = codeBox.Text?.Trim() ?? "";
            if (code == shocker.ShareCode) return;

            // A new code may point at another shocker: look it up again.
            shocker.ShareCode = code;
            if (code.Length > 0)
            {
                shocker.HubId = 0;
                shocker.ShockerId = 0;
            }

            SettingsService.Save();
            PiShockService.NotifyStatusChanged();
        }

        codeBox.LostFocus += (_, _) => CommitCode();

        var toggle = new ToggleSwitch { IsChecked = shocker.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };

        var maxLabel = Ui.Text($"{shocker.MaxIntensity}%", "muted", 12);
        var max = new Slider { Minimum = 1, Maximum = 100, Value = shocker.MaxIntensity };
        max.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            shocker.MaxIntensity = (int)Math.Round(max.Value);
            maxLabel.Text = $"{shocker.MaxIntensity}%";
            SettingsService.SaveSoon();
        };

        var durationLabel = Ui.Text($"{shocker.MaxDurationSeconds} s", "muted", 12);
        var duration = new Slider { Minimum = 1, Maximum = 15, Value = shocker.MaxDurationSeconds, TickFrequency = 1, IsSnapToTickEnabled = true };
        duration.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            shocker.MaxDurationSeconds = (int)Math.Round(duration.Value);
            durationLabel.Text = $"{shocker.MaxDurationSeconds} s";
            SettingsService.SaveSoon();
        };

        var tests = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var op in new[] { PiShockOp.Beep, PiShockOp.Vibrate, PiShockOp.Shock })
        {
            var button = new Button
            {
                Classes = { "bt", "small" },
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new PackIconLucide { Kind = op.Icon(), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = op.DisplayName(), VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            if (op == PiShockOp.Shock) button.Classes.Add("danger");
            button.Click += async (_, _) =>
            {
                // Pick up a name or code that was typed but not committed yet.
                CommitCode();
                if (PiShockService.TestShocker(shocker, op) is { } error) await NotificationService.ShowAsync(error, 4000);
            };
            tests.Children.Add(button);
        }

        var delete = Ui.IconButton(PackIconLucideKind.Trash2, "Remove shocker");
        var confirm = false;
        delete.Click += (_, _) =>
        {
            if (!confirm)
            {
                confirm = true;
                delete.Content = Ui.Text("Remove?", "body", 12);
                delete.Classes.Add("danger");
                return;
            }

            Settings.Shockers.Remove(shocker);
            SettingsService.Save();
            BuildShockerList();
            PiShockService.NotifyStatusChanged();
        };

        var row = new Border { Classes = { "row" }, Opacity = shocker.Enabled ? 1 : 0.6 };
        toggle.IsCheckedChanged += (_, _) =>
        {
            shocker.Enabled = toggle.IsChecked == true;
            row.Opacity = shocker.Enabled ? 1 : 0.6;
            SettingsService.Save();
            PiShockService.NotifyStatusChanged();
        };

        row.Child = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,12,*,Auto,Auto"),
                    Children =
                    {
                        IconTile(PackIconLucideKind.Zap, Amber, AmberSoft),
                        Ui.WithColumn(nameBox, 1),
                        Ui.WithColumn(codeBox, 3),
                        Ui.WithColumn(toggle, 4),
                        Ui.WithColumn(delete, 5)
                    }
                },
                new Grid
                {
                    Margin = new Thickness(54, 0, 0, 0),
                    ColumnDefinitions = new ColumnDefinitions("*,24,*"),
                    Children =
                    {
                        Ui.SliderBlock("Maximum intensity", maxLabel, max),
                        Ui.WithColumn(Ui.SliderBlock("Maximum duration", durationLabel, duration), 2)
                    }
                },
                new Grid
                {
                    Margin = new Thickness(54, 0, 0, 0),
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Children = { tests }
                }
            }
        };

        return row;
    }

    #endregion

    private static Border IconTile(PackIconLucideKind kind, IBrush foreground, IBrush background) => new()
    {
        Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
        Margin = new Thickness(0, 0, 14, 0),
        Background = background,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new PackIconLucide
        {
            Kind = kind, Width = 18, Height = 18, Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        }
    };
}
