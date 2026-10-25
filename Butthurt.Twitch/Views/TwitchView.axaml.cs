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
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Twitch.Views;

public partial class TwitchView : UserControl, IPageView
{
    public string Title => "Twitch";
    public PackIconLucideKind Icon => PackIconLucideKind.Twitch;
    public SideBarType SidebarType => SideBarType.Streaming;
    public event EventHandler? BackRequested;

    private static readonly IBrush TwitchPurple = new SolidColorBrush(Color.Parse("#BF94FF"));

    private readonly HashSet<string> _expandedRules = new();
    private List<string> _rewardTitles = new();
    private bool _loading;

    public TwitchView()
    {
        InitializeComponent();

        AddRuleButton.Flyout = CreateAddRuleMenu();

        AutoConnectToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            TwitchPlugin.Settings.AutoConnect = AutoConnectToggle.IsChecked == true;
            SettingsService.Save();
        };

        ClientIdBox.LostFocus += (_, _) =>
        {
            var value = ClientIdBox.Text?.Trim() ?? "";
            if (value == TwitchPlugin.Settings.ClientId) return;
            TwitchPlugin.Settings.ClientId = value;
            SettingsService.Save();
            UpdateAccount();
        };

        TwitchService.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateAccount);
        TwitchService.RewardsChanged += (_, _) => Dispatcher.UIThread.Post(RefreshRewards);

        AttachedToVisualTree += async (_, _) =>
        {
            _loading = true;
            AutoConnectToggle.IsChecked = TwitchPlugin.Settings.AutoConnect;
            ClientIdBox.Text = TwitchPlugin.Settings.ClientId;
            _loading = false;

            UpdateAccount();
            BuildRuleList();
            RefreshRewards();

            // Reward names for the redeem rules' suggestions, only available for affiliates and partners.
            _rewardTitles = await TwitchService.GetRewardTitles();
        };
    }

    #region Account

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TwitchService.IsLoggedIn)
        {
            await TwitchService.Logout();
            return;
        }

        var login = TwitchService.Login();

        // The code appears right after the first request, open the page as soon as it's there.
        for (var i = 0; i < 50 && TwitchService.PendingLogin == null && !login.IsCompleted; i++)
        {
            await System.Threading.Tasks.Task.Delay(100);
        }

        if (TwitchService.PendingLogin is { } code) OpenUrl(code.VerificationUri);

        if (await login)
        {
            await NotificationService.ShowAsync($"Logged in as {TwitchService.ChannelName}");
            _rewardTitles = await TwitchService.GetRewardTitles();
        }
    }

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TwitchService.State is TwitchState.Connected or TwitchState.Connecting) await TwitchService.Disconnect();
        else await TwitchService.Connect();
    }

    private void OpenLoginPage_Click(object? sender, RoutedEventArgs e)
    {
        if (TwitchService.PendingLogin is { } code) OpenUrl(code.VerificationUri);
    }

    private void CancelLogin_Click(object? sender, RoutedEventArgs e) => TwitchService.CancelLogin();

    private void OpenConsole_Click(object? sender, RoutedEventArgs e) => OpenUrl("https://dev.twitch.tv/console/apps");

    private void UpdateAccount()
    {
        // Vibration rules only exist with toy features on.
        VibrationRulesCard.IsVisible = PluginManager.ToysEnabled;
        PageSubtitle.Text = PluginManager.ToysEnabled
            ? "Turn bits, subs, redeems, raids and chat commands into vibrations. You decide exactly what counts."
            : "Connect your channel so bits, subs, redeems, raids and chat commands can drive your other plugins, like VRChat.";

        var state = TwitchService.State;
        var loggedIn = TwitchService.IsLoggedIn;

        var (pill, brushKey) = state switch
        {
            TwitchState.Connected => ("LIVE", "SuccessBrush"),
            TwitchState.Connecting => ("CONNECTING", "WarningBrush"),
            TwitchState.WaitingForLogin => ("WAITING FOR LOGIN", "WarningBrush"),
            TwitchState.Error when loggedIn => ("OFFLINE", "DangerBrush"),
            _ when loggedIn => ("OFFLINE", "DimBrush"),
            _ => ("NOT LOGGED IN", "DimBrush")
        };
        StatePillText.Text = pill;
        StatePillText.Foreground = Ui.Brush(brushKey);

        AccountText.Text = state switch
        {
            TwitchState.Connected => $"Listening to twitch.tv/{TwitchService.ChannelName}",
            _ when TwitchService.LastError != null => TwitchService.LastError,
            _ when loggedIn => $"Logged in as {TwitchService.ChannelName}",
            _ when !TwitchService.HasClientId => "Add a Twitch Client ID below to log in.",
            _ => "Log in so Butthurt can see your bits, subs, redeems and chat."
        };

        LoginButton.Content = loggedIn ? "Log out" : "Log in with Twitch";
        LoginButton.Classes.Set("primary", !loggedIn);
        LoginButton.Classes.Set("ghost", loggedIn);
        LoginButton.IsEnabled = state != TwitchState.WaitingForLogin && (loggedIn || TwitchService.HasClientId);

        ConnectButton.IsVisible = loggedIn;
        ConnectButton.Content = state is TwitchState.Connected or TwitchState.Connecting ? "Disconnect" : "Connect";

        LoginCodePanel.IsVisible = state == TwitchState.WaitingForLogin && TwitchService.PendingLogin != null;
        LoginCodeText.Text = TwitchService.PendingLogin?.UserCode;

        var usingDefault = string.IsNullOrWhiteSpace(TwitchPlugin.Settings.ClientId) && TwitchService.HasClientId;
        ClientIdHint.Text = usingDefault
            ? "Butthurt's own Twitch app is used. Only fill this in if you registered your own."
            : "Butthurt needs a Twitch app to log in. Open the Twitch console, register an application " +
              "(category: Application Integration, client type: Public, OAuth redirect URL: http://localhost) and paste its Client ID here.";
        // Without a Client ID nothing works, so show the field. Otherwise it's an advanced option.
        if (!TwitchService.HasClientId) _showClientId = true;
        ClientIdPanel.IsVisible = _showClientId;
        ClientIdToggle.IsVisible = !_showClientId;

        RefreshRewards();
    }

    private bool _showClientId;

    private void ClientIdToggle_Click(object? sender, RoutedEventArgs e)
    {
        _showClientId = true;
        UpdateAccount();
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    #endregion

    #region Channel point rewards

    private async void CreateReward_Click(object? sender, RoutedEventArgs e)
    {
        var title = NewRewardTitle.Text?.Trim() ?? "";
        if (title.Length == 0)
        {
            await NotificationService.ShowAsync("Give the reward a name first.");
            return;
        }

        CreateRewardButton.IsEnabled = false;
        var error = await TwitchService.CreateReward(title, (int)(NewRewardCost.Value ?? 500));
        CreateRewardButton.IsEnabled = true;

        if (error != null)
        {
            await NotificationService.ShowAsync($"Couldn't create the reward: {error}", 5000);
            return;
        }

        NewRewardTitle.Text = "";
        _rewardTitles = await TwitchService.GetRewardTitles();
        RefreshRewards();
        await NotificationService.ShowAsync($"\"{title}\" is live on your channel. Use it in a redeem rule.");
    }

    private void RefreshRewards()
    {
        RewardList.Children.Clear();
        foreach (var reward in TwitchPlugin.Settings.ManagedRewards.ToList())
        {
            var delete = Ui.IconButton(PackIconLucideKind.Trash2, "Delete from Twitch");
            var confirm = false;
            delete.Click += async (_, _) =>
            {
                if (!confirm)
                {
                    confirm = true;
                    delete.Content = Ui.Text("Delete?", "body", 12);
                    delete.Classes.Add("danger");
                    return;
                }

                var error = await TwitchService.DeleteReward(reward);
                if (error != null) await NotificationService.ShowAsync($"Couldn't delete it: {error}", 5000);
                RefreshRewards();
            };

            RewardList.Children.Add(new Border
            {
                Classes = { "row" },
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    Children =
                    {
                        new PackIconLucide
                        {
                            Kind = PackIconLucideKind.Coins, Width = 18, Height = 18, Foreground = TwitchPurple,
                            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0)
                        },
                        Ui.WithColumn(new StackPanel
                        {
                            Spacing = 2,
                            Children =
                            {
                                Ui.Text(reward.Title, "h3"),
                                Ui.Text(reward.OwnedByRules
                                    ? $"{reward.Cost:N0} points · made for a rule{(reward.IsEnabled ? "" : " · hidden while the rule is off")}"
                                    : $"{reward.Cost:N0} points · managed by Butthurt", "muted", 12)
                            }
                        }, 1),
                        // Rule rewards come and go with their rule, deleting them here would only bring them back.
                        Ui.WithColumn<Control>(reward.OwnedByRules
                            ? Ui.Text("Delete the rule to remove it", "muted", 11)
                            : delete, 2)
                    }
                }
            });
        }

        var canManage = TwitchService.CanManageRedeems;
        ReauthBanner.IsVisible = TwitchService.IsLoggedIn && !canManage;
        CreateRewardButton.IsEnabled = canManage;
        NewRewardTitle.IsEnabled = canManage;
        NewRewardCost.IsEnabled = canManage;
    }

    #endregion

    #region Rules

    private static List<TwitchRule> Rules => TwitchPlugin.Settings.Rules!;

    private MenuFlyout CreateAddRuleMenu()
    {
        var menu = new MenuFlyout();
        foreach (var trigger in Enum.GetValues<TwitchTrigger>())
        {
            var item = new MenuItem
            {
                Header = trigger.DisplayName(),
                Icon = new PackIconLucide { Kind = trigger.Icon(), Width = 14, Height = 14 }
            };
            item.Click += (_, _) => AddRule(trigger);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void AddRule(TwitchTrigger trigger)
    {
        var rule = new TwitchRule
        {
            Name = trigger.DisplayName(),
            Trigger = trigger,
            Match = trigger == TwitchTrigger.ChatCommand ? "!buzz" : "",
            CooldownSeconds = trigger == TwitchTrigger.ChatCommand ? 30 : 0
        };

        Rules.Add(rule);
        _expandedRules.Add(rule.Id);
        SettingsService.Save();
        BuildRuleList();
    }

    private void BuildRuleList()
    {
        RuleList.Children.Clear();
        foreach (var rule in Rules)
        {
            RuleList.Children.Add(CreateRuleRow(rule));
        }

        NoRulesText.IsVisible = Rules.Count == 0;
    }

    private Control CreateRuleRow(TwitchRule rule)
    {
        var nameText = Ui.Text(rule.Name, "h3");
        var summaryText = Ui.Text(TwitchRules.Summary(rule), "muted", 12);

        void Changed()
        {
            nameText.Text = string.IsNullOrWhiteSpace(rule.Name) ? rule.Trigger.DisplayName() : rule.Name;
            summaryText.Text = TwitchRules.Summary(rule);
            SettingsService.SaveSoon();
        }

        var toggle = new ToggleSwitch
        {
            IsChecked = rule.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center
        };

        var test = new Button { Classes = { "bt", "small" }, Content = "Test", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += async (_, _) =>
        {
            if (!ButthurtService.IsConnected) await NotificationService.ShowAsync("Connect to Intiface Central to feel the test.");
            TwitchService.Test(rule);
        };

        var expanded = _expandedRules.Contains(rule.Id);
        var edit = Ui.IconButton(expanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil, "Edit");
        var delete = Ui.IconButton(PackIconLucideKind.Trash2, "Delete");

        var editor = CreateEditor(rule, Changed);
        editor.IsVisible = expanded;

        var body = new StackPanel { Spacing = 12 };
        var row = new Border { Classes = { "row" }, Child = body, Opacity = rule.Enabled ? 1 : 0.6 };

        toggle.IsCheckedChanged += (_, _) =>
        {
            rule.Enabled = toggle.IsChecked == true;
            row.Opacity = rule.Enabled ? 1 : 0.6;
            SettingsService.Save();
        };

        edit.Click += (_, _) =>
        {
            editor.IsVisible = !editor.IsVisible;
            if (editor.IsVisible) _expandedRules.Add(rule.Id);
            else _expandedRules.Remove(rule.Id);
            ((PackIconLucide)edit.Content!).Kind = editor.IsVisible ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil;
        };

        var confirmDelete = false;
        delete.Click += (_, _) =>
        {
            // Two clicks, so a misclick doesn't throw away a carefully tuned rule.
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

        body.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
            Children =
            {
                new Border
                {
                    Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 14, 0),
                    Background = new SolidColorBrush(Color.Parse("#269146FF")),
                    Child = new PackIconLucide
                    {
                        Kind = rule.Trigger.Icon(), Width = 18, Height = 18, Foreground = TwitchPurple,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                },
                Ui.WithColumn(new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2,
                    Children = { nameText, summaryText }
                }, 1),
                Ui.WithColumn(toggle, 2),
                Ui.WithColumn(test, 3),
                Ui.WithColumn(edit, 4),
                Ui.WithColumn(delete, 5)
            }
        });
        body.Children.Add(editor);

        return row;
    }

    /// <summary>The fields for one rule: the shared Twitch condition plus how it should buzz.</summary>
    private Control CreateEditor(TwitchRule rule, Action changed)
    {
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(54, 4, 0, 4) };

        var name = new TextBox { Classes = { "field" }, Text = rule.Name, Watermark = rule.Trigger.DisplayName() };
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text ?? "";
            changed();
        };
        fields.Children.Add(Ui.Field("Name", name));
        fields.Children.Add(TwitchConditionEditor.Create(rule, changed, () => _rewardTitles));

        // How it feels
        var intensityLabel = Ui.Text($"{rule.Intensity * 100:0}%", "muted", 12);
        var intensity = new Slider { Minimum = 5, Maximum = 100, Value = rule.Intensity * 100 };
        intensity.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            rule.Intensity = Math.Round(intensity.Value) / 100.0;
            intensityLabel.Text = $"{intensity.Value:0}%";
            changed();
        };

        var durationLabel = Ui.Text(Ui.FormatDuration(rule.DurationMs), "muted", 12);
        var duration = new Slider { Minimum = 100, Maximum = 10000, Value = rule.DurationMs };
        duration.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            rule.DurationMs = (int)(Math.Round(duration.Value / 100) * 100);
            durationLabel.Text = Ui.FormatDuration(rule.DurationMs);
            changed();
        };

        fields.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Children =
            {
                Ui.SliderBlock("Intensity", intensityLabel, intensity),
                Ui.WithColumn(Ui.SliderBlock("Duration", durationLabel, duration), 2)
            }
        });

        var extras = new WrapPanel { ItemSpacing = 24, LineSpacing = 8 };
        if (rule.Trigger.HasAmount())
        {
            var scale = new CheckBox { Content = "Bigger amounts buzz longer (up to 5×)", IsChecked = rule.ScaleWithAmount };
            scale.IsCheckedChanged += (_, _) =>
            {
                rule.ScaleWithAmount = scale.IsChecked == true;
                changed();
            };
            extras.Children.Add(scale);
        }

        extras.Children.Add(TwitchConditionEditor.Cooldown(rule, changed));
        fields.Children.Add(extras);

        return fields;
    }

    #endregion
}
