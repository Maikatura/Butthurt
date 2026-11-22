using System;
using System.Collections.Generic;
using System.Linq;
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
            UpdateAnnouncements();
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

        ChatAnnounceToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Settings.PostAnnouncementsInChat = ChatAnnounceToggle.IsChecked == true;
            SettingsService.Save();
            UpdateAnnouncements();
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
            ChatAnnounceToggle.IsChecked = Settings.PostAnnouncementsInChat;
            _loading = false;

            UpdateAnnouncements();
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

    #region Announcements

    private void UpdateAnnouncements()
    {
        var overlayOn = PluginManager.IsOn(PluginManager.OverlayPluginId);
        OpenOverlayButton.IsVisible = overlayOn;
        OverlayLinkText.Text = overlayOn
            ? "Placement, size and the OBS address are on the Stream overlay page."
            : "Turn on Stream overlay in Settings to show announcements and the queue in OBS.";

        ChatAnnounceText.Text = !TwitchService.IsLoggedIn ? "Log in to Twitch first. Posted as you, in your own chat."
            : !TwitchService.CanSendChat ? "Log out of Twitch and in again so Butthurt may post in chat."
            : "Posted as you, in your own chat. Tests are never posted.";
    }

    private void OpenOverlay_Click(object? sender, RoutedEventArgs e) => NavigationService.NavigateToPlugin(PluginManager.OverlayPluginId);

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
