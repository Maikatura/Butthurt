using System;
using System.Collections.Generic;
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
using Butthurt.Fansly.Models;
using Butthurt.Fansly.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Fansly.Views;

public partial class FanslyView : UserControl, IPageView
{
    public string Title => "Fansly";
    public PackIconLucideKind Icon => PackIconLucideKind.Radio;
    public SideBarType SidebarType => SideBarType.Streaming;
    public event EventHandler? BackRequested;

    private static readonly IBrush FanslyBlue = new SolidColorBrush(Color.Parse("#5CB3FA"));

    private readonly HashSet<string> _expandedRules = new();
    private bool _loading;

    public FanslyView()
    {
        InitializeComponent();

        AddRuleButton.Flyout = CreateAddRuleMenu();

        AutoConnectToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            FanslyPlugin.Settings.AutoConnect = AutoConnectToggle.IsChecked == true;
            SettingsService.Save();
        };

        FanslyService.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateAccount);
        PluginManager.PluginsChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            UpdateAccount();
            BuildExtraSections();
        });

        AttachedToVisualTree += (_, _) =>
        {
            _loading = true;
            AutoConnectToggle.IsChecked = FanslyPlugin.Settings.AutoConnect;
            UsernameBox.Text = FanslyPlugin.Settings.Username;
            _loading = false;

            UpdateAccount();
            BuildRuleList();
            BuildExtraSections();
        };
    }

    #region Channel

    private async void SaveUsername_Click(object? sender, RoutedEventArgs e)
    {
        var name = UsernameBox.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            await FanslyService.ClearChannel();
            UpdateAccount();
            return;
        }

        SaveUsernameButton.IsEnabled = false;
        var error = await FanslyService.SetChannel(name);
        SaveUsernameButton.IsEnabled = true;

        if (error != null)
        {
            await NotificationService.ShowAsync(error, 5000);
            return;
        }

        UsernameBox.Text = FanslyPlugin.Settings.Username;
        UpdateAccount();
        // The tier pickers list the creator's tiers, which may have just been looked up.
        BuildRuleList();
        await NotificationService.ShowAsync($"Found {FanslyService.ChannelName} on Fansly");
    }

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (FanslyService.State is FanslyState.Connected or FanslyState.Connecting) await FanslyService.Disconnect();
        else await FanslyService.Connect();
    }

    private void TokenToggle_Click(object? sender, RoutedEventArgs e)
    {
        TokenPanel.IsVisible = true;
        TokenToggle.IsVisible = false;
    }

    private async void SaveToken_Click(object? sender, RoutedEventArgs e)
    {
        var token = TokenBox.Text?.Trim() ?? "";
        if (token.Length == 0) return;

        FanslyService.SetToken(token);
        TokenBox.Text = "";
        UpdateAccount();
        await NotificationService.ShowAsync("Token saved. Reconnect to use it.");
    }

    private void ClearToken_Click(object? sender, RoutedEventArgs e)
    {
        FanslyService.SetToken(null);
        TokenBox.Text = "";
        UpdateAccount();
    }

    private void UpdateAccount()
    {
        // Toy rules only matter with the Toys plugin on ("Toys" is Butthurt.Toys' plugin id).
        VibrationRulesCard.IsVisible = PluginManager.IsOn("Toys");
        PageSubtitle.Text = PluginManager.ToysEnabled
            ? "Turn tips, subscriptions and chat commands from your Fansly livestream into vibrations (or PiShock zaps)."
            : "Connect your Fansly livestream chat so tips, subscriptions and chat commands can drive your other plugins.";

        var state = FanslyService.State;
        var setUp = FanslyService.IsSetUp;

        var (pill, brushKey) = state switch
        {
            FanslyState.Connected => ("LIVE", "SuccessBrush"),
            FanslyState.Connecting => ("CONNECTING", "WarningBrush"),
            FanslyState.Error => ("OFFLINE", "DangerBrush"),
            _ when setUp => ("OFFLINE", "DimBrush"),
            _ => ("NOT SET UP", "DimBrush")
        };
        StatePillText.Text = pill;
        StatePillText.Foreground = Ui.Brush(brushKey);

        AccountText.Text = state switch
        {
            FanslyState.Connected when FanslyService.LastError != null => FanslyService.LastError,
            FanslyState.Connected => $"Listening to {FanslyService.ChannelName}'s chat",
            _ when FanslyService.LastError != null => FanslyService.LastError,
            _ when setUp => $"Ready to listen to {FanslyService.ChannelName}",
            _ => "Type your Fansly username below."
        };

        ConnectButton.IsEnabled = setUp;
        ConnectButton.Content = state is FanslyState.Connected or FanslyState.Connecting ? "Disconnect" : "Connect";

        ClearTokenButton.IsVisible = FanslyService.HasToken;
        TokenBox.Watermark = FanslyService.HasToken ? "A token is saved. Paste a new one to replace it" : "Session token";
        if (FanslyService.HasToken)
        {
            TokenPanel.IsVisible = true;
            TokenToggle.IsVisible = false;
        }
    }

    #endregion

    /// <summary>Cards other plugins put on this page, e.g. PiShock rules. Rebuilt when plugins are turned on or off.</summary>
    private void BuildExtraSections()
    {
        ExtraSections.Children.Clear();
        foreach (var card in PageSections.CreateFor(FanslyPlugin.PluginId)) ExtraSections.Children.Add(card);
    }

    #region Rules

    private static List<FanslyRule> Rules => FanslyPlugin.Settings.Rules!;

    private MenuFlyout CreateAddRuleMenu()
    {
        var menu = new MenuFlyout();
        foreach (var trigger in Enum.GetValues<FanslyTrigger>())
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

    private void AddRule(FanslyTrigger trigger)
    {
        var rule = new FanslyRule
        {
            Name = trigger.DisplayName(),
            Trigger = trigger,
            Match = trigger == FanslyTrigger.ChatCommand ? "!buzz" : "",
            CooldownSeconds = trigger == FanslyTrigger.ChatCommand ? 30 : 0
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

    private Control CreateRuleRow(FanslyRule rule)
    {
        var nameText = Ui.Text(rule.Name, "h3");
        var summaryText = Ui.Text(FanslyRules.Summary(rule), "muted", 12);

        void Changed()
        {
            nameText.Text = string.IsNullOrWhiteSpace(rule.Name) ? rule.Trigger.DisplayName() : rule.Name;
            summaryText.Text = FanslyRules.Summary(rule);
            SettingsService.SaveSoon();
        }

        var toggle = new ToggleSwitch { IsChecked = rule.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };

        var test = new Button { Classes = { "bt", "small" }, Content = "Test", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += async (_, _) =>
        {
            if (!ButthurtService.IsConnected) await NotificationService.ShowAsync("Connect to Intiface Central to feel the test.");
            FanslyService.Test(rule);
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
                    Background = new SolidColorBrush(Color.Parse("#1A2B40")),
                    Child = new PackIconLucide
                    {
                        Kind = rule.Trigger.Icon(), Width = 18, Height = 18, Foreground = FanslyBlue,
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

    private static Control CreateEditor(FanslyRule rule, Action changed)
    {
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(54, 4, 0, 4) };

        var name = new TextBox { Classes = { "field" }, Text = rule.Name, Watermark = rule.Trigger.DisplayName() };
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text ?? "";
            changed();
        };
        fields.Children.Add(Ui.Field("Name", name));
        fields.Children.Add(FanslyConditionEditor.Create(rule, changed));

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
            var scale = new CheckBox { Content = "Bigger tips buzz longer (up to 5×)", IsChecked = rule.ScaleWithAmount };
            scale.IsCheckedChanged += (_, _) =>
            {
                rule.ScaleWithAmount = scale.IsChecked == true;
                changed();
            };
            extras.Children.Add(scale);
        }

        extras.Children.Add(FanslyConditionEditor.Cooldown(rule, changed));
        fields.Children.Add(extras);

        return fields;
    }

    #endregion
}
