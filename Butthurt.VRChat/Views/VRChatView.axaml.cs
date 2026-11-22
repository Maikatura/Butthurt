using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Twitch;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.Twitch.Views;
using Butthurt.VRChat.Models;
using Butthurt.VRChat.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.VRChat.Views;

public partial class VRChatView : UserControl, IPageView
{
    public string Title => "VRChat";
    public PackIconLucideKind Icon => PackIconLucideKind.Glasses;
    public SideBarType SidebarType => SideBarType.Plugins;
    public event EventHandler? BackRequested;

    private static readonly IBrush Lilac = new SolidColorBrush(Color.Parse("#FAFAFA"));

    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _loading;

    private static VRChatSettings Settings => VRChatPlugin.Settings;
    private static List<VRChatRule> Rules => Settings.Rules!;

    public VRChatView()
    {
        InitializeComponent();

        SetUpDefaultAvatarPicker();
        VRChatService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        PluginManager.PluginsChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            UpdateRuleLinks();
            BuildTriggerSections();
        });
        _countdown.Tick += (_, _) => UpdateStatus();

        BaseHeightBox.ValueChanged += (_, e) =>
        {
            if (_loading || e.NewValue is not { } value) return;
            Settings.BaseEyeHeight = (double)value;
            SettingsService.SaveSoon();
            UpdateStatus();
        };
        LearnHeightCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Settings.LearnBaseHeight = LearnHeightCheck.IsChecked == true;
            SettingsService.Save();
        };
        SmoothSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var seconds = Math.Round(SmoothSlider.Value * 4) / 4; // quarter seconds
            SmoothText.Text = seconds <= 0 ? "Instant" : $"{seconds:0.##} s";
            if (_loading) return;
            Settings.SmoothSeconds = seconds;
            SettingsService.SaveSoon();
        };
        MinMultiplierBox.ValueChanged += (_, e) =>
        {
            if (_loading || e.NewValue is not { } value) return;
            Settings.MinMultiplier = (double)value;
            SettingsService.SaveSoon();
        };
        MaxMultiplierBox.ValueChanged += (_, e) =>
        {
            if (_loading || e.NewValue is not { } value) return;
            Settings.MaxMultiplier = (double)value;
            SettingsService.SaveSoon();
        };
        DefaultAvatarBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            Settings.DefaultAvatarId = DefaultAvatarBox.Text?.Trim() ?? "";
            SettingsService.SaveSoon();
        };
        AutoStartToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Settings.AutoStart = AutoStartToggle.IsChecked == true;
            SettingsService.Save();
        };

        // Stream overlay (a core feature, the VRChat page is where it's set up)
        void OverlayChanged()
        {
            if (_loading) return;
            var overlay = SettingsService.Current.Overlay;
            overlay.Enabled = OverlayToggle.IsChecked == true;
            overlay.ShowQueue = OverlayQueueCheck.IsChecked == true;
            overlay.ShowAlerts = OverlayAlertsCheck.IsChecked == true;
            overlay.AlertSeconds = (int)(AlertSecondsBox.Value ?? 6);
            overlay.Port = (int)(OverlayPortBox.Value ?? 7878);
            SettingsService.Save();
            OverlayService.ApplySettings();
            Announcer.PushQueue();
            UpdateOverlay();
        }

        OverlayToggle.IsCheckedChanged += (_, _) => OverlayChanged();
        OverlayQueueCheck.IsCheckedChanged += (_, _) => OverlayChanged();
        OverlayAlertsCheck.IsCheckedChanged += (_, _) => OverlayChanged();
        AlertSecondsBox.ValueChanged += (_, _) => OverlayChanged();
        OverlayPortBox.LostFocus += (_, _) => OverlayChanged();
        OverlayService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateOverlay);

        OverlayScaleSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var scale = Math.Round(OverlayScaleSlider.Value * 10) / 10;
            OverlayScaleText.Text = $"{scale * 100:0}%";
            if (_loading) return;
            SettingsService.Current.Overlay.Scale = scale;
            SettingsService.SaveSoon();
            OverlayService.ApplySettings();
        };

        ChatAnnounceToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Settings.PostAnnouncementsInChat = ChatAnnounceToggle.IsChecked == true;
            SettingsService.Save();
            UpdateOverlay();
        };

        AttachedToVisualTree += (_, _) =>
        {
            _loading = true;
            BaseHeightBox.Value = (decimal)Settings.BaseEyeHeight;
            LearnHeightCheck.IsChecked = Settings.LearnBaseHeight;
            SmoothSlider.Value = Settings.SmoothSeconds;
            SmoothText.Text = Settings.SmoothSeconds <= 0 ? "Instant" : $"{Settings.SmoothSeconds:0.##} s";
            MinMultiplierBox.Value = (decimal)Settings.MinMultiplier;
            MaxMultiplierBox.Value = (decimal)Settings.MaxMultiplier;
            DefaultAvatarBox.Text = Settings.DefaultAvatarId;
            AutoStartToggle.IsChecked = Settings.AutoStart;
            OscText.Text = $"Sends to {Settings.OscHost}:{Settings.SendPort}, listens on port {Settings.ListenPort} (VRChat's defaults).";
            var overlay = SettingsService.Current.Overlay;
            OverlayToggle.IsChecked = overlay.Enabled;
            OverlayQueueCheck.IsChecked = overlay.ShowQueue;
            OverlayAlertsCheck.IsChecked = overlay.ShowAlerts;
            AlertSecondsBox.Value = overlay.AlertSeconds;
            OverlayPortBox.Value = overlay.Port;
            ChatAnnounceToggle.IsChecked = Settings.PostAnnouncementsInChat;
            OverlayScaleSlider.Value = overlay.Scale;
            OverlayScaleText.Text = $"{overlay.Scale * 100:0}%";
            BuildPositionPickers();
            _loading = false;

            UpdateOverlay();
            UpdateStatus();
            UpdateRuleLinks();
            BuildTriggerSections();
            _countdown.Start();

        };
        DetachedFromVisualTree += (_, _) => _countdown.Stop();
    }

    #region Status

    private void StartStop_Click(object? sender, RoutedEventArgs e)
    {
        if (VRChatService.IsRunning) VRChatService.Stop();
        else VRChatService.Start();
    }

    private void Reset_Click(object? sender, RoutedEventArgs e)
    {
        VRChatService.ResetSize();
        VRChatService.ResetParameters();
        VRChatService.ResetAvatars();
    }

    /// <summary>"Your own avatar": a pick list of your avatars next to the ID box.</summary>
    private void SetUpDefaultAvatarPicker()
    {
        if (DefaultAvatarBox.Parent is not Grid grid) return;

        var pick = VRChatRulesCard.PickButton(AvatarLibrary.Get(Settings.DefaultAvatarId)?.Name is { Length: > 0 } known ? known : "Pick avatar",
            VRChatRulesCard.AvatarChoices, a => a.Name.Length > 0 ? a.Name : a.Id,
            a => (a.Id == VRChatService.CurrentAvatarId ? "wearing now · " : "") + a.Id,
            "No avatars found. Turn on OSC in VRChat and wear an avatar once.", avatar => DefaultAvatarBox.Text = avatar.Id);
        DefaultAvatarBox.TextChanged += (_, _) =>
            pick.Content = AvatarLibrary.Get(DefaultAvatarBox.Text)?.Name is { Length: > 0 } name ? name : "Pick avatar";

        grid.Children.Add(Ui.WithColumn(pick, 1));
    }

    private void UpdateStatus()
    {
        var running = VRChatService.IsRunning;
        var vrchatOpen = running && VRChatService.IsVRChatRunning;

        var (pill, brushKey) = !running ? ("STOPPED", "DimBrush")
            : !vrchatOpen ? ("WAITING FOR VRCHAT", "WarningBrush")
            : VRChatService.IsConnected ? ("CONNECTED", "SuccessBrush")
            : ("SENDING", "SuccessBrush");
        StatePillText.Text = pill;
        StatePillText.Foreground = Ui.Brush(brushKey);

        StateText.Text = !running ? "Press Start to talk to VRChat."
            : !vrchatOpen ? "Start VRChat and turn on OSC (Action Menu → Options → OSC)."
            : VRChatService.IsConnected ? "VRChat is talking back, everything works."
            : !VRChatService.IsListening ? "Sending to VRChat. Another OSC app has port " + Settings.ListenPort + ", so your avatar and height can't be read."
            : "Sending to VRChat. Nothing heard back yet, check that OSC is on in VRChat.";

        StartStopButton.Content = running ? "Stop" : "Start";
        StartStopButton.Classes.Set("primary", !running);
        StartStopButton.Classes.Set("danger", running);

        var multiplier = VRChatService.CurrentMultiplier;
        SizeText.Text = $"×{multiplier:0.##}";
        HeightText.Text = $"{Settings.BaseEyeHeight * multiplier:0.00} m eye height (normal {Settings.BaseEyeHeight:0.00} m)";

        AvatarText.Text = VRChatService.CurrentAvatarId != null ? AvatarLibrary.DisplayName(VRChatService.CurrentAvatarId) : "Unknown";
        AvatarHint.Text = VRChatService.CurrentAvatarId ?? "Switch avatars in VRChat once so Butthurt can see it";

        // One pill per rule (with a count when it stacked), all on one fixed-height row.
        EffectList.Children.Clear();
        var now = DateTime.UtcNow;

        foreach (var group in VRChatService.ActiveEffects.GroupBy(e => e.RuleId).OrderBy(g => g.Min(e => e.ExpiresUtc)))
        {
            var ruleMultiplier = group.Aggregate(1.0, (product, e) => product * e.Multiplier);
            var count = group.Count();
            var text = $"{group.First().Name}" + (count > 1 ? $" ×{count}" : "") +
                       $"  ·  ×{ruleMultiplier:0.##}  ·  {SecondsLeft(group.Min(e => e.ExpiresUtc), now)}";
            EffectList.Children.Add(EffectPill(ruleMultiplier >= 1 ? PackIconLucideKind.TrendingUp : PackIconLucideKind.Shrink, text,
                count > 1 ? $"{count} stacked, the next one ends in {SecondsLeft(group.Min(e => e.ExpiresUtc), now)}, the last in {SecondsLeft(group.Max(e => e.ExpiresUtc), now)}" : null));
        }

        foreach (var group in VRChatService.ActiveParameters.GroupBy(e => e.Rule.Id).OrderBy(g => g.Min(e => e.ExpiresUtc)))
        {
            var rule = group.First().Rule;
            var count = group.Count();
            var name = rule.ParameterNameFor(VRChatService.CurrentAvatarId) ?? "no name on this avatar";
            var text = $"{rule.Name}" + (count > 1 ? $" ×{count}" : "") + $"  ·  {name}  ·  {SecondsLeft(group.Min(e => e.ExpiresUtc), now)}";
            EffectList.Children.Add(EffectPill(PackIconLucideKind.UserRoundCog, text, null));
        }

        if (VRChatService.CurrentSwap is { } swap)
        {
            EffectList.Children.Add(EffectPill(PackIconLucideKind.Shirt,
                $"{AvatarLibrary.DisplayName(swap.Swap.AvatarId)}  ·  {SecondsLeft(swap.EndsUtc, now)}",
                $"Then back to {AvatarLibrary.DisplayName(VRChatService.HomeAvatarId)} unless someone else is waiting"));
        }

        var waiting = VRChatService.AvatarQueue;
        if (waiting.Count > 0)
        {
            EffectList.Children.Add(EffectPill(PackIconLucideKind.Users,
                "Up next: " + string.Join(", ", waiting.Select(w => AvatarLibrary.DisplayName(w.AvatarId))),
                "Avatar swaps take turns, each starts when the one before it is over."));
        }

        NoEffectsText.IsVisible = EffectList.Children.Count == 0;

        ResetButton.IsEnabled = running && (VRChatService.ActiveEffects.Count > 0 || VRChatService.ActiveParameters.Count > 0 ||
                                            VRChatService.CurrentSwap != null || VRChatService.AvatarQueue.Count > 0);
    }

    private static string SecondsLeft(DateTime expiresUtc, DateTime now) =>
        expiresUtc == DateTime.MaxValue ? "until reset" : $"{Math.Max(0, (int)Math.Ceiling((expiresUtc - now).TotalSeconds))}s";

    private static Control EffectPill(PackIconLucideKind icon, string text, string? tooltip)
    {
        var pill = new Border
        {
            Classes = { "pill" },
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new PackIconLucide { Kind = icon, Width = 13, Height = 13, Foreground = Lilac, VerticalAlignment = VerticalAlignment.Center },
                    Ui.Text(text, "muted", 12)
                }
            }
        };
        if (tooltip != null) ToolTip.SetTip(pill, tooltip);
        return pill;
    }

    #region Overlay

    private void UpdateOverlay()
    {
        var overlay = SettingsService.Current.Overlay;
        OverlayOptions.IsVisible = overlay.Enabled;
        OverlayUrlBox.Text = OverlayService.Url;

        var clients = OverlayService.ConnectedClients;
        var (pill, brushKey) = !overlay.Enabled ? ("OFF", "DimBrush")
            : !OverlayService.IsRunning ? ("PROBLEM", "DangerBrush")
            : clients > 0 ? (clients == 1 ? "1 SOURCE CONNECTED" : $"{clients} SOURCES CONNECTED", "SuccessBrush")
            : ("WAITING FOR OBS", "WarningBrush");
        OverlayPillText.Text = pill;
        OverlayPillText.Foreground = Ui.Brush(brushKey);
        if (overlay.Enabled && !OverlayService.IsRunning && OverlayService.LastError != null)
        {
            ToolTip.SetTip(OverlayPillText, OverlayService.LastError);
        }

        ChatAnnounceText.Text = !TwitchService.IsLoggedIn ? "Log in to Twitch first. Posted as you, in your own chat."
            : !TwitchService.CanSendChat ? "Log out of Twitch and in again so Butthurt may post in chat."
            : "Posted as you, in your own chat. Tests are never posted.";
    }

    private void BuildPositionPickers()
    {
        var overlay = SettingsService.Current.Overlay;

        AlertPositionHost.Content = PositionPicker(overlay.AlertPosition, Enum.GetValues<OverlayPosition>(), position =>
        {
            overlay.AlertPosition = position;
            SettingsService.Save();
            OverlayService.ApplySettings();
            BuildPositionPickers();
        });

        // The queue is a tall list, it only makes sense in a corner.
        QueuePositionHost.Content = PositionPicker(overlay.QueuePosition,
            new[] { OverlayPosition.TopLeft, OverlayPosition.TopRight, OverlayPosition.BottomLeft, OverlayPosition.BottomRight }, position =>
            {
                overlay.QueuePosition = position;
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
            var isAllowed = allowed.Contains(position);
            var selected = position == current;

            var spot = new Button
            {
                Classes = { "bt" },
                Padding = new Thickness(0),
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsVisible = isAllowed,
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

    private async void CopyOverlayUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(OverlayService.Url);
            await NotificationService.ShowAsync("Overlay address copied. Paste it into an OBS Browser source.");
        }
    }

    private void OpenOverlay_Click(object? sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OverlayService.Url) { UseShellExecute = true });

    private async void OpenOverlayFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!System.IO.Directory.Exists(OverlayService.Folder))
        {
            await NotificationService.ShowAsync("Turn on the OBS overlay once to create its folder.");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OverlayService.Folder) { UseShellExecute = true });
        await NotificationService.ShowAsync("Careful: changing the overlay files can break the overlay. Read README.txt first.");
    }

    private async void TestAnnouncement_Click(object? sender, RoutedEventArgs e)
    {
        if (!OverlayService.IsRunning)
        {
            await NotificationService.ShowAsync("Turn on the OBS overlay first.");
            return;
        }

        var rule = Rules.FirstOrDefault(r => r.Enabled && r.Announce) ?? Rules.FirstOrDefault();
        var text = rule != null
            ? rule.Source == VRChatSource.Fansly
                ? Announcer.Text(rule, Fansly.Services.FanslyRules.SampleFor(rule.Fansly), VRChatService.CurrentMultiplier, VRChatService.CurrentAvatarId)
                : Announcer.Text(rule, TwitchRules.SampleFor(rule.When), VRChatService.CurrentMultiplier, VRChatService.CurrentAvatarId)
            : "TestUser cheered 50 bits: ×1.1 for 30s!";
        OverlayService.Alert(text);
    }

    #endregion

    /// <summary>Where the stream rules are: buttons to the Twitch and Fansly pages, for the plugins that are on.</summary>
    private void UpdateRuleLinks()
    {
        var twitch = VRChatPlugin.IsTwitchPluginOn;
        var fansly = VRChatPlugin.IsFanslyPluginOn;
        OpenTwitchRulesButton.IsVisible = twitch;
        OpenFanslyRulesButton.IsVisible = fansly;

        var count = Rules.Count;
        RuleLinksText.Text = !twitch && !fansly
            ? "What Twitch and Fansly do in VRChat is set on their pages. Turn one of them on in Settings first."
            : $"What {(twitch && fansly ? "Twitch and Fansly" : twitch ? "Twitch" : "Fansly")} can do in VRChat (grow, switch avatar, " +
              $"change a parameter) is set on {(twitch && fansly ? "their pages" : "its page")}, under VRChat rules. " +
              $"{count} rule{(count == 1 ? "" : "s")} so far.";
    }

    private void OpenTwitchRules_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateToPlugin(TwitchPlugin.PluginId);

    private void OpenFanslyRules_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateToPlugin(Fansly.FanslyPlugin.PluginId);

    /// <summary>
    /// What VRChat itself makes happen: toy rules for avatar parameters (with the Toys plugin on), then cards other
    /// plugins put here, like PiShock rules.
    /// </summary>
    private void BuildTriggerSections()
    {
        TriggerSections.Children.Clear();
        if (PluginManager.IsOn("Toys")) TriggerSections.Children.Add(new VRChatToyRulesCard());
        foreach (var card in PageSections.CreateFor(VRChatPlugin.PluginId)) TriggerSections.Children.Add(card);
    }

    #endregion
}
