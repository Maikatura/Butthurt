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

    private static readonly IBrush Lilac = new SolidColorBrush(Color.Parse("#D8B4FE"));

    private readonly HashSet<string> _expandedRules = new();
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<string> _rewardTitles = new();
    private bool _loading;

    private static VRChatSettings Settings => VRChatPlugin.Settings;
    private static List<VRChatRule> Rules => Settings.Rules!;

    public VRChatView()
    {
        InitializeComponent();

        AddRuleButton.Flyout = CreateAddRuleMenu();
        SetUpDefaultAvatarPicker();
        VRChatService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        TwitchService.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateTwitchBanner);
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

        AttachedToVisualTree += async (_, _) =>
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
            UpdateTwitchBanner();
            BuildRuleList();
            _countdown.Start();

            _rewardTitles = await TwitchService.GetRewardTitles();
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

        var pick = PickButton(AvatarLibrary.Get(Settings.DefaultAvatarId)?.Name is { Length: > 0 } known ? known : "Pick avatar",
            AvatarChoices, a => a.Name.Length > 0 ? a.Name : a.Id,
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

    /// <summary>"Announce this rule", the message with {placeholders} and a live preview.</summary>
    private static Control AnnouncementEditor(VRChatRule rule, Action changed)
    {
        var preview = Ui.Text("", "body", 13);
        preview.Foreground = new SolidColorBrush(Color.Parse("#E9D5FF"));

        var template = new TextBox
        {
            Classes = { "field" },
            Text = rule.AnnouncementTemplate,
            Watermark = VRChatRule.DefaultAnnouncement(rule.Action),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false
        };

        void UpdatePreview()
        {
            var sample = TwitchRules.SampleFor(rule.When) with { UserName = "TestUser" };
            var size = rule.Action == VRChatAction.Grow ? VRChatService.CurrentMultiplier * rule.Multiplier : VRChatService.CurrentMultiplier;
            preview.Text = Announcer.Text(rule, sample, size, VRChatService.CurrentAvatarId);
        }

        template.TextChanged += (_, _) =>
        {
            rule.AnnouncementTemplate = template.Text ?? "";
            changed();
            UpdatePreview();
        };

        var announce = new CheckBox { Content = "Announce this rule on the overlay (and in chat if that's turned on)", IsChecked = rule.Announce };
        var details = new StackPanel { Spacing = 8, IsVisible = rule.Announce };
        announce.IsCheckedChanged += (_, _) =>
        {
            rule.Announce = announce.IsChecked == true;
            details.IsVisible = rule.Announce;
            changed();
        };

        // Clickable placeholders: clicking one adds it where the cursor is.
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (name, meaning) in Announcer.Placeholders)
        {
            var chip = new Button { Classes = { "bt", "small" }, Content = "{" + name + "}", Padding = new Thickness(8, 3), FontSize = 11 };
            ToolTip.SetTip(chip, meaning);
            chip.Click += (_, _) =>
            {
                var text = template.Text ?? "";
                if (text.Length == 0) text = template.Watermark ?? "";
                var caret = Math.Clamp(template.CaretIndex, 0, text.Length);
                template.Text = text.Insert(caret, "{" + name + "}");
                template.CaretIndex = caret + name.Length + 2;
                template.Focus();
            };
            chips.Children.Add(chip);
        }

        details.Children.Add(Ui.Field("Message (leave empty for the one shown greyed out)", template));
        details.Children.Add(chips);
        details.Children.Add(new Border
        {
            Classes = { "codeblock" },
            Padding = new Thickness(12, 8),
            Child = new StackPanel { Spacing = 2, Children = { Ui.Text("PREVIEW", "caption"), preview } }
        });
        UpdatePreview();

        return new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 0), Children = { announce, details } };
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
                Background = selected ? Ui.Brush("AccentGradient") : new SolidColorBrush(Color.Parse("#22222C")),
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

    private async void TestAnnouncement_Click(object? sender, RoutedEventArgs e)
    {
        if (!OverlayService.IsRunning)
        {
            await NotificationService.ShowAsync("Turn on the OBS overlay first.");
            return;
        }

        var rule = Rules.FirstOrDefault(r => r.Enabled && r.Announce) ?? Rules.FirstOrDefault();
        var text = rule != null
            ? Announcer.Text(rule, TwitchRules.SampleFor(rule.When), VRChatService.CurrentMultiplier, VRChatService.CurrentAvatarId)
            : "TestUser cheered 50 bits: ×1.1 for 30s!";
        OverlayService.Alert(text);
    }

    #endregion

    private void UpdateTwitchBanner()
    {
        string? text = null, button = null;
        if (!VRChatPlugin.IsTwitchPluginOn)
        {
            text = "VRChat rules react to Twitch, but the Twitch plugin is turned off.";
            button = "Open Settings";
        }
        else if (!TwitchService.IsLoggedIn)
        {
            text = "Log in to Twitch so your rules get bits, subs and redeems.";
            button = "Open Twitch";
        }
        else if (TwitchService.State != TwitchState.Connected)
        {
            text = "Twitch isn't connected right now, so rules wait for it.";
            button = "Open Twitch";
        }

        TwitchBanner.IsVisible = text != null;
        TwitchBannerText.Text = text;
        TwitchBannerButton.Content = button;
    }

    private void TwitchBanner_Click(object? sender, RoutedEventArgs e)
    {
        if (!VRChatPlugin.IsTwitchPluginOn) NavigationService.NavigateTo<SettingsView>();
        else NavigationService.NavigateToPlugin(TwitchPlugin.PluginId);
    }

    #endregion

    #region Rules

    private MenuFlyout CreateAddRuleMenu()
    {
        var menu = new MenuFlyout();
        foreach (var (action, label, icon) in new[]
                 {
                     (VRChatAction.Grow, "Grow or shrink", PackIconLucideKind.TrendingUp),
                     (VRChatAction.ChangeAvatar, "Change avatar", PackIconLucideKind.Shirt),
                     (VRChatAction.SetParameter, "Set avatar parameter", PackIconLucideKind.UserRoundCog)
                 })
        {
            var item = new MenuItem { Header = label, Icon = new PackIconLucide { Kind = icon, Width = 14, Height = 14 } };
            item.Click += (_, _) => AddRule(action);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void AddRule(VRChatAction action)
    {
        var rule = new VRChatRule
        {
            Name = action switch
            {
                VRChatAction.Grow => "Grow",
                VRChatAction.SetParameter => "Avatar parameter",
                _ => "Change avatar"
            },
            Action = action,
            When = new TwitchCondition { Trigger = TwitchTrigger.Bits, MinAmount = action == VRChatAction.Grow ? 20 : 100 }
        };

        Rules.Add(rule);
        _expandedRules.Add(rule.Id);
        SettingsService.Save();
        BuildRuleList();
    }

    private void BuildRuleList()
    {
        RuleList.Children.Clear();
        foreach (var rule in Rules) RuleList.Children.Add(CreateRuleRow(rule));
        NoRulesText.IsVisible = Rules.Count == 0;
    }

    private static string Summary(VRChatRule rule)
    {
        var action = rule.Action switch
        {
            VRChatAction.ChangeAvatar => string.IsNullOrWhiteSpace(rule.AvatarId)
                ? "switch avatar (no avatar set yet)"
                : $"switch to {AvatarLibrary.DisplayName(rule.AvatarId)}" + (rule.RevertAfterSeconds > 0 ? $", back after {rule.RevertAfterSeconds}s" : ""),
            VRChatAction.SetParameter => ParameterSummary(rule),
            _ => $"×{rule.Multiplier:0.##} for {rule.DurationSeconds}s" + (rule.Stack ? ", stacks" : ", restarts timer")
        };

        return $"{TwitchRules.ConditionSummary(rule.When)} → {action}";
    }

    private static string ParameterSummary(VRChatRule rule)
    {
        var name = rule.ParameterNameFor(null) ?? rule.Parameters.FirstOrDefault(p => p.Name.Length > 0)?.Name ?? "no parameter set";
        var value = rule.ParameterType == ParameterType.Bool ? (rule.ParameterValue >= 0.5 ? "on" : "off")
            : rule.ParameterMode == ParameterMode.Add ? $"+{rule.ParameterValue:0.##}"
            : $"{rule.ParameterValue:0.##}";
        var overrides = rule.Parameters.Count(p => !string.IsNullOrWhiteSpace(p.AvatarId));
        return $"{name} = {value}" +
               (rule.DurationSeconds > 0 ? $" for {rule.DurationSeconds}s" : " until reset") +
               (overrides > 0 ? $" · {overrides} avatar-specific name{(overrides == 1 ? "" : "s")}" : "");
    }

    private static string Shorten(string id) => id.Length > 18 ? id[..18] + "…" : id;

    private Control CreateRuleRow(VRChatRule rule)
    {
        var nameText = Ui.Text(rule.Name, "h3");
        var summaryText = Ui.Text(Summary(rule), "muted", 12);

        void Changed()
        {
            nameText.Text = string.IsNullOrWhiteSpace(rule.Name) ? "Rule" : rule.Name;
            summaryText.Text = Summary(rule);
            SettingsService.SaveSoon();
        }

        var toggle = new ToggleSwitch { IsChecked = rule.Enabled };
        var test = new Button { Classes = { "bt", "small" }, Content = "Test", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += async (_, _) =>
        {
            if (!VRChatPlugin.Test(rule)) await NotificationService.ShowAsync("Couldn't run it, see Recent activity on the Home page for why.");
        };

        var expanded = _expandedRules.Contains(rule.Id);
        var edit = Ui.IconButton(expanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil, "Edit");
        var delete = Ui.IconButton(PackIconLucideKind.Trash2, "Delete");

        var editorHost = new ContentControl { IsVisible = expanded };
        void BuildEditor() => editorHost.Content = CreateEditor(rule, Changed, BuildEditor);
        BuildEditor();

        var row = new Border { Classes = { "row" }, Opacity = rule.Enabled ? 1 : 0.6 };

        toggle.IsCheckedChanged += (_, _) =>
        {
            rule.Enabled = toggle.IsChecked == true;
            row.Opacity = rule.Enabled ? 1 : 0.6;
            SettingsService.Save();
        };

        edit.Click += (_, _) =>
        {
            editorHost.IsVisible = !editorHost.IsVisible;
            if (editorHost.IsVisible) _expandedRules.Add(rule.Id);
            else _expandedRules.Remove(rule.Id);
            ((PackIconLucide)edit.Content!).Kind = editorHost.IsVisible ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil;
        };

        var confirmDelete = false;
        delete.Click += (_, _) =>
        {
            if (!confirmDelete)
            {
                confirmDelete = true;
                delete.Content = Ui.Text("Delete?", "body", 12);
                delete.Classes.Add("danger");
                return;
            }

            Rules.Remove(rule);
            _expandedRules.Remove(rule.Id);
            SettingsService.Save();
            BuildRuleList();
        };

        row.Child = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
                    Children =
                    {
                        new Border
                        {
                            Classes = { "iconbox" },
                            Margin = new Thickness(0, 0, 14, 0),
                            Child = new PackIconLucide
                            {
                                Kind = rule.Action switch
                                {
                                    VRChatAction.ChangeAvatar => PackIconLucideKind.Shirt,
                                    VRChatAction.SetParameter => PackIconLucideKind.UserRoundCog,
                                    _ => rule.Multiplier >= 1 ? PackIconLucideKind.TrendingUp : PackIconLucideKind.Shrink
                                },
                                Width = 18, Height = 18, Foreground = Lilac,
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                            }
                        },
                        Ui.WithColumn(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { nameText, summaryText } }, 1),
                        Ui.WithColumn(toggle, 2),
                        Ui.WithColumn(test, 3),
                        Ui.WithColumn(edit, 4),
                        Ui.WithColumn(delete, 5)
                    }
                },
                editorHost
            }
        };

        return row;
    }

    /// <summary>Name, the Twitch condition and the VRChat action. Rebuilt when the trigger changes.</summary>
    private Control CreateEditor(VRChatRule rule, Action changed, Action rebuild)
    {
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(54, 4, 0, 4) };

        var name = new TextBox { Classes = { "field" }, Text = rule.Name, Watermark = "Rule name" };
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text ?? "";
            changed();
        };
        fields.Children.Add(Ui.Field("Name", name));

        fields.Children.Add(TwitchConditionEditor.Create(rule.When, changed, () => _rewardTitles, rebuild));

        if (rule.Action == VRChatAction.ChangeAvatar)
        {
            fields.Children.Add(Ui.Field("Avatar (pick one you've worn, or paste an avtr_… ID from vrchat.com)",
                AvatarField(rule.AvatarId, id =>
                {
                    rule.AvatarId = id;
                    changed();
                })));

            fields.Children.Add(Ui.Field("Switch back after (seconds, 0 = keep the new avatar)",
                Ui.Number(rule.RevertAfterSeconds, 0, 86400, value =>
                {
                    rule.RevertAfterSeconds = value;
                    changed();
                })));
        }
        else if (rule.Action == VRChatAction.SetParameter)
        {
            fields.Children.Add(ParameterEditor(rule, changed, rebuild));
        }
        else
        {
            var multiplier = new NumericUpDown
            {
                Value = (decimal)rule.Multiplier, Minimum = 0.1m, Maximum = 10m, Increment = 0.05m, FormatString = "0.00",
                MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left
            };
            multiplier.ValueChanged += (_, e) =>
            {
                if (e.NewValue is not { } value) return;
                rule.Multiplier = (double)value;
                changed();
            };

            var duration = Ui.Number(rule.DurationSeconds, 1, 86400, value =>
            {
                rule.DurationSeconds = value;
                changed();
            });

            fields.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,24,*"),
                Children =
                {
                    Ui.Field("Size multiplier (1.1 = 10% bigger, 0.5 = half size)", multiplier),
                    Ui.WithColumn(Ui.Field("For how long (seconds)", duration), 2)
                }
            });

            var stack = new CheckBox { Content = "Stack: every trigger multiplies again (×1.1 twice = ×1.21). Off: it only restarts the timer.", IsChecked = rule.Stack };
            stack.IsCheckedChanged += (_, _) =>
            {
                rule.Stack = stack.IsChecked == true;
                changed();
            };
            fields.Children.Add(stack);
        }

        fields.Children.Add(TwitchConditionEditor.Cooldown(rule.When, changed));
        fields.Children.Add(AnnouncementEditor(rule, changed));
        return fields;
    }

    /// <summary>Type, value, how long, and the parameter name per avatar.</summary>
    private Control ParameterEditor(VRChatRule rule, Action changed, Action rebuild)
    {
        var panel = new StackPanel { Spacing = 12 };

        var type = new ComboBox
        {
            ItemsSource = new[] { "On/off (bool)", "Whole number (int, 0-255)", "Decimal (float, -1 to 1)" },
            SelectedIndex = (int)rule.ParameterType,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedIndex < 0 || (ParameterType)type.SelectedIndex == rule.ParameterType) return;
            rule.ParameterType = (ParameterType)type.SelectedIndex;
            changed();
            rebuild();
        };

        var mode = new ComboBox
        {
            ItemsSource = new[] { "Set to the value", "Add the value (stacks)" },
            SelectedIndex = (int)rule.ParameterMode,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = rule.ParameterType != ParameterType.Bool
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;
            rule.ParameterMode = (ParameterMode)mode.SelectedIndex;
            changed();
        };

        panel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Children = { Ui.Field("Parameter type", type), Ui.WithColumn(Ui.Field("What to do", mode), 2) }
        });

        panel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*,24,*"),
            Children =
            {
                Ui.Field("Value", ValueInput(rule.ParameterType, rule.ParameterValue, v =>
                {
                    rule.ParameterValue = v;
                    changed();
                })),
                Ui.WithColumn(Ui.Field("When it ends, back to", ValueInput(rule.ParameterType, rule.ParameterResetValue, v =>
                {
                    rule.ParameterResetValue = v;
                    changed();
                })), 2),
                Ui.WithColumn(Ui.Field("For how long (seconds, 0 = until reset)", Ui.Number(rule.DurationSeconds, 0, 86400, v =>
                {
                    rule.DurationSeconds = v;
                    changed();
                })), 4)
            }
        });

        // Parameter names, per avatar
        var targets = new StackPanel { Spacing = 8 };
        void BuildTargets()
        {
            targets.Children.Clear();
            foreach (var target in rule.Parameters.ToList())
            {
                targets.Children.Add(TargetRow(rule, target, changed, BuildTargets, rebuild));
            }
        }

        BuildTargets();

        var addTarget = new Button { Classes = { "bt", "small" }, HorizontalAlignment = HorizontalAlignment.Left };
        addTarget.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new PackIconLucide { Kind = PackIconLucideKind.Plus, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "Different name on another avatar", VerticalAlignment = VerticalAlignment.Center }
            }
        };
        addTarget.Click += (_, _) =>
        {
            rule.Parameters.Add(new ParameterTarget { AvatarId = VRChatService.CurrentAvatarId ?? "" });
            changed();
            BuildTargets();
        };

        panel.Children.Add(Ui.Field(
            "Parameter name. Avatars name things differently: add a name per avatar, the one without an avatar is used everywhere else. " +
            "Active changes are sent again after every avatar switch, using the new avatar's name.",
            new StackPanel { Spacing = 8, Children = { targets, addTarget } }));

        return panel;
    }

    private Control TargetRow(VRChatRule rule, ParameterTarget target, Action changed, Action rebuildTargets, Action rebuildEditor)
    {
        var anyAvatar = string.IsNullOrWhiteSpace(target.AvatarId) &&
                        rule.Parameters.First(p => string.IsNullOrWhiteSpace(p.AvatarId)) == target;

        // The avatar whose parameters to suggest: this row's avatar, or the one you're wearing for "any avatar".
        IReadOnlyList<AvatarParameterInfo> Suggestions() => ParametersFor(anyAvatar ? VRChatService.CurrentAvatarId : target.AvatarId);

        var name = new AutoCompleteBox
        {
            Text = target.Name,
            Watermark = "Parameter name, e.g. BoobSize",
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            ItemsSource = Suggestions().Select(p => p.Name).ToList()
        };
        name.GotFocus += (_, _) => name.ItemsSource = Suggestions().Select(p => p.Name).ToList();
        name.PropertyChanged += (_, e) =>
        {
            if (e.Property != AutoCompleteBox.TextProperty) return;
            target.Name = name.Text?.Trim() ?? "";
            changed();
        };

        var pickParameter = PickButton("Pick", Suggestions, p => p.Name, p => p.Type.ToString(),
            "No parameters found. Wear the avatar in VRChat once with OSC on.", parameter =>
            {
                name.Text = parameter.Name;
                if (rule.ParameterType != parameter.Type)
                {
                    // Match the rule to the parameter, a bool parameter can't take a decimal.
                    rule.ParameterType = parameter.Type;
                    changed();
                    rebuildEditor();
                }
            });
        var namePart = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { name, Ui.WithColumn(pickParameter, 1) } };

        Control avatarPart;
        if (anyAvatar)
        {
            avatarPart = new Border
            {
                Classes = { "pill" },
                VerticalAlignment = VerticalAlignment.Center,
                Child = Ui.Text("Any avatar", "muted", 12)
            };
        }
        else
        {
            avatarPart = AvatarField(target.AvatarId, id =>
            {
                target.AvatarId = id;
                changed();
            });
        }

        var remove = Ui.IconButton(PackIconLucideKind.Trash2, "Remove");
        remove.IsVisible = !anyAvatar;
        remove.Click += (_, _) =>
        {
            rule.Parameters.Remove(target);
            changed();
            rebuildTargets();
        };

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.3*,12,*,Auto"),
            Children = { avatarPart, Ui.WithColumn(namePart, 2), Ui.WithColumn(remove, 3) }
        };
    }

    /// <summary>Settable parameters of an avatar, or of every known avatar when it's unknown.</summary>
    private static IReadOnlyList<AvatarParameterInfo> ParametersFor(string? avatarId)
    {
        if (AvatarLibrary.Get(avatarId) is { } avatar) return avatar.WritableParameters.ToList();

        return AvatarLibrary.All.SelectMany(a => a.WritableParameters)
            .Concat(VRChatService.SeenParameters.Select(n => new AvatarParameterInfo(n, ParameterType.Float, true)))
            .GroupBy(p => p.Name).Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>An avatar ID box with a list of your avatars by name next to it.</summary>
    private static Control AvatarField(string avatarId, Action<string> changed)
    {
        var box = new TextBox { Classes = { "field" }, Text = avatarId, Watermark = "avtr_…" };
        var pick = PickButton(AvatarLibrary.Get(avatarId)?.Name is { Length: > 0 } known ? known : "Pick avatar",
            AvatarChoices, a => a.Name.Length > 0 ? a.Name : a.Id,
            a => (a.Id == VRChatService.CurrentAvatarId ? "wearing now · " : "") + a.Id,
            "No avatars found. Turn on OSC in VRChat and wear an avatar once.", avatar => box.Text = avatar.Id);

        box.TextChanged += (_, _) =>
        {
            var id = box.Text?.Trim() ?? "";
            changed(id);
            pick.Content = AvatarLibrary.Get(id)?.Name is { Length: > 0 } name ? name : "Pick avatar";
        };

        return new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { box, Ui.WithColumn(pick, 1) } };
    }

    /// <summary>The avatar you're wearing first, then the rest by when you last wore them.</summary>
    private static IReadOnlyList<AvatarInfo> AvatarChoices()
    {
        var all = AvatarLibrary.All;
        var current = all.FirstOrDefault(a => a.Id == VRChatService.CurrentAvatarId);
        return current == null ? all : all.Where(a => a != current).Prepend(current).ToList();
    }

    /// <summary>A button that opens a searchable list and reports the picked item.</summary>
    private static Button PickButton<T>(string label, Func<IReadOnlyList<T>> items, Func<T, string> title, Func<T, string> subtitle,
        string emptyText, Action<T> picked)
    {
        var button = new Button
        {
            Classes = { "bt", "small" },
            Content = label,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180
        };

        var search = new TextBox { Classes = { "field" }, Watermark = "Search…" };
        var list = new ListBox { MaxHeight = 340, Width = 380 };
        var empty = Ui.Text(emptyText, "muted", 12);
        empty.MaxWidth = 380;
        var flyout = new Flyout { Content = new StackPanel { Spacing = 8, Margin = new Thickness(4), Children = { search, empty, list } } };

        void Fill()
        {
            var filter = search.Text?.Trim() ?? "";
            var matches = items()
                .Where(i => filter.Length == 0 ||
                            title(i).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            subtitle(i).Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(1000)
                .Select(i => new ListBoxItem
                {
                    Tag = i,
                    Content = new StackPanel { Spacing = 1, Children = { Ui.Text(title(i), "h3"), Ui.Text(subtitle(i), "muted", 11) } }
                })
                .ToList();
            list.ItemsSource = matches;
            empty.IsVisible = matches.Count == 0;
        }

        search.TextChanged += (_, _) => Fill();
        flyout.Opened += (_, _) =>
        {
            search.Text = "";
            Fill();
            search.Focus();
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: T item }) return;
            flyout.Hide();
            picked(item);
        };

        button.Flyout = flyout;
        return button;
    }

    /// <summary>A checkbox for bools, a number box for ints and floats.</summary>
    private static Control ValueInput(ParameterType type, double value, Action<double> changed)
    {
        if (type == ParameterType.Bool)
        {
            var check = new CheckBox { Content = "On", IsChecked = value >= 0.5 };
            check.IsCheckedChanged += (_, _) => changed(check.IsChecked == true ? 1 : 0);
            return check;
        }

        var box = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = type == ParameterType.Int ? 0 : -1,
            Maximum = type == ParameterType.Int ? 255 : 1,
            Increment = type == ParameterType.Int ? 1 : 0.05m,
            FormatString = type == ParameterType.Int ? "0" : "0.00",
            MinWidth = 130,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v) changed((double)v);
        };
        return box;
    }

    #endregion
}
