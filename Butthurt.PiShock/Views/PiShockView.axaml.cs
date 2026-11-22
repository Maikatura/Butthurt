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
    public SideBarType SidebarType => SideBarType.Header;
    public event EventHandler? BackRequested;

    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FBBF24"));
    private static readonly IBrush AmberSoft = new SolidColorBrush(Color.Parse("#26F59E0B"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush RedSoft = new SolidColorBrush(Color.Parse("#26EF4444"));

    private readonly HashSet<string> _expandedRules = new();
    private List<string> _rewardTitles = new();
    private bool _loading;

    private static PiShockSettings Settings => PiShockPlugin.Settings;
    private static List<PiShockRule> Rules => Settings.Rules!;

    public PiShockView()
    {
        InitializeComponent();

        AddRuleButton.Flyout = CreateAddRuleMenu();

        UsernameBox.LostFocus += (_, _) =>
        {
            var value = UsernameBox.Text?.Trim() ?? "";
            if (value == Settings.Username) return;
            Settings.Username = value;
            SettingsService.Save();
            PiShockService.NotifyStatusChanged();
        };

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

        AttachedToVisualTree += async (_, _) =>
        {
            _loading = true;
            UsernameBox.Text = Settings.Username;
            SenderNameBox.Text = Settings.SenderName;
            ShocksAllowedToggle.IsChecked = Settings.ShocksAllowed;
            PausedToggle.IsChecked = Settings.Paused;
            _loading = false;

            UpdateAccount();
            BuildShockerList();
            BuildRuleList();

            if (TwitchService.IsLoggedIn) _rewardTitles = await TwitchService.GetRewardTitles();
        };
    }

    #region Account

    private void OpenAccount_Click(object? sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://login.pishock.com/account") { UseShellExecute = true });

    private async void SaveApiKey_Click(object? sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Text?.Trim() ?? "";
        if (key.Length == 0) return;

        // The username box saves on lost focus, make sure it's in before the key is used.
        Settings.Username = UsernameBox.Text?.Trim() ?? "";
        PiShockService.SetApiKey(key);
        ApiKeyBox.Text = "";
        UpdateAccount();
        await NotificationService.ShowAsync("API key saved. Use a shocker's Beep button to check it works.");
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

        AccountText.Text = !PiShockService.HasLogin ? "Add your pishock.com username and API key."
            : PiShockService.LastError ?? (PiShockService.LastAction is { } last
                ? $"Last: {last} at {PiShockService.LastActionTime:HH:mm:ss}"
                : $"Logged in as {Settings.Username}");

        ApiKeyBox.Watermark = string.IsNullOrEmpty(Settings.ApiKey) ? "API key" : "A key is saved. Paste a new one to replace it";

        var sources = new List<string>();
        if (PiShockPlugin.IsTwitchPluginOn) sources.Add("Twitch");
        if (PiShockPlugin.IsFanslyPluginOn) sources.Add("Fansly");
        RulesHint.Text = sources.Count == 0
            ? "Turn on the Twitch or Fansly plugin in Settings, rules react to their events."
            : $"When something happens on {string.Join(" or ", sources)}, every matching rule fires. Commands for one shocker take turns.";
    }

    #endregion

    #region Shockers

    private async void AddShocker_Click(object? sender, RoutedEventArgs e)
    {
        var code = NewShockerCode.Text?.Trim() ?? "";
        if (code.Length == 0)
        {
            await NotificationService.ShowAsync("Paste the shocker's share code first.");
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
        BuildRuleList();
        UpdateAccount();
        PiShockService.NotifyStatusChanged();
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
            BuildRuleList();
        };

        var codeBox = new TextBox { Classes = { "field" }, Text = shocker.ShareCode, Watermark = "Share code", PasswordChar = '•' };
        codeBox.LostFocus += (_, _) =>
        {
            shocker.ShareCode = codeBox.Text?.Trim() ?? "";
            SettingsService.Save();
            PiShockService.NotifyStatusChanged();
        };

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
                shocker.ShareCode = codeBox.Text?.Trim() ?? shocker.ShareCode;
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
            BuildRuleList();
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

    #region Rules

    private MenuFlyout CreateAddRuleMenu()
    {
        var menu = new MenuFlyout();

        var twitch = new MenuItem { Header = "Twitch", Icon = new PackIconLucide { Kind = PackIconLucideKind.Twitch, Width = 14, Height = 14 } };
        foreach (var trigger in Enum.GetValues<TwitchTrigger>())
        {
            var item = new MenuItem { Header = trigger.DisplayName(), Icon = new PackIconLucide { Kind = trigger.Icon(), Width = 14, Height = 14 } };
            item.Click += (_, _) => AddRule(PiShockSource.Twitch, twitchTrigger: trigger);
            twitch.Items.Add(item);
        }

        var fansly = new MenuItem { Header = "Fansly", Icon = new PackIconLucide { Kind = PackIconLucideKind.Radio, Width = 14, Height = 14 } };
        foreach (var trigger in Enum.GetValues<FanslyTrigger>())
        {
            var item = new MenuItem { Header = trigger.DisplayName(), Icon = new PackIconLucide { Kind = trigger.Icon(), Width = 14, Height = 14 } };
            item.Click += (_, _) => AddRule(PiShockSource.Fansly, fanslyTrigger: trigger);
            fansly.Items.Add(item);
        }

        menu.Items.Add(twitch);
        menu.Items.Add(fansly);
        return menu;
    }

    private void AddRule(PiShockSource source, TwitchTrigger twitchTrigger = TwitchTrigger.Bits, FanslyTrigger fanslyTrigger = FanslyTrigger.Tip)
    {
        var chat = source == PiShockSource.Twitch ? twitchTrigger == TwitchTrigger.ChatCommand : fanslyTrigger == FanslyTrigger.ChatCommand;
        var rule = new PiShockRule
        {
            Source = source,
            Name = $"{source} {(source == PiShockSource.Twitch ? twitchTrigger.DisplayName() : fanslyTrigger.DisplayName()).ToLowerInvariant()}",
            Op = PiShockOp.Vibrate,
            Intensity = 25,
            DurationSeconds = 1,
            Twitch = new TwitchCondition
            {
                Trigger = twitchTrigger,
                MinAmount = twitchTrigger == TwitchTrigger.Bits ? 100 : 1,
                Match = chat ? "!zap" : "",
                CooldownSeconds = chat ? 30 : 0
            },
            Fansly = new FanslyCondition
            {
                Trigger = fanslyTrigger,
                MinTip = 5,
                Match = chat ? "!zap" : "",
                CooldownSeconds = chat ? 30 : 0
            }
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

    private Control CreateRuleRow(PiShockRule rule)
    {
        var nameText = Ui.Text(rule.Name, "h3");
        var summaryText = Ui.Text(PiShockService.Summary(rule), "muted", 12);
        summaryText.TextWrapping = TextWrapping.Wrap;
        var icon = new PackIconLucide
        {
            Kind = rule.Op.Icon(), Width = 18, Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0), Child = icon };

        void UpdateLook()
        {
            var shock = rule.Op == PiShockOp.Shock;
            icon.Kind = rule.Op.Icon();
            icon.Foreground = shock ? Red : Amber;
            tile.Background = shock ? RedSoft : AmberSoft;
        }

        UpdateLook();

        void Changed()
        {
            nameText.Text = string.IsNullOrWhiteSpace(rule.Name) ? rule.Op.DisplayName() : rule.Name;
            summaryText.Text = PiShockService.Summary(rule);
            UpdateLook();
            SettingsService.SaveSoon();
        }

        var toggle = new ToggleSwitch { IsChecked = rule.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };

        var test = new Button { Classes = { "bt", "small" }, Content = "Test", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += async (_, _) =>
        {
            if (!PiShockService.HasLogin) await NotificationService.ShowAsync("Add your PiShock username and API key first.");
            else if (!PiShockService.Test(rule)) await NotificationService.ShowAsync("Nothing was sent, check the activity log.", 4000);
        };

        var expanded = _expandedRules.Contains(rule.Id);
        var edit = Ui.IconButton(expanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil, "Edit");
        var delete = Ui.IconButton(PackIconLucideKind.Trash2, "Delete");

        // The editor is rebuilt when the source or trigger changes, so only the right fields show.
        var editorHost = new Border { IsVisible = expanded };
        void Rebuild() => editorHost.Child = CreateEditor(rule, Changed, Rebuild);
        Rebuild();

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

        body.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
            Children =
            {
                tile,
                Ui.WithColumn(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { nameText, summaryText } }, 1),
                Ui.WithColumn(toggle, 2),
                Ui.WithColumn(test, 3),
                Ui.WithColumn(edit, 4),
                Ui.WithColumn(delete, 5)
            }
        });
        body.Children.Add(editorHost);

        return row;
    }

    private Control CreateEditor(PiShockRule rule, Action changed, Action rebuild)
    {
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(54, 4, 0, 4) };

        var name = new TextBox { Classes = { "field" }, Text = rule.Name, Watermark = rule.Op.DisplayName() };
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text ?? "";
            changed();
        };
        fields.Children.Add(Ui.Field("Name", name));

        // Where the events come from
        var sources = Enum.GetValues<PiShockSource>();
        var source = new ComboBox
        {
            ItemsSource = sources.Select(s => s.ToString()).ToArray(),
            SelectedIndex = Array.IndexOf(sources, rule.Source),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        source.SelectionChanged += (_, _) =>
        {
            if (source.SelectedIndex < 0 || sources[source.SelectedIndex] == rule.Source) return;
            rule.Source = sources[source.SelectedIndex];
            changed();
            rebuild();
        };
        fields.Children.Add(Ui.Field("React to", source));

        fields.Children.Add(rule.Source == PiShockSource.Twitch
            ? TwitchConditionEditor.Create(rule.Twitch, changed, () => _rewardTitles, rebuild)
            : FanslyConditionEditor.Create(rule.Fansly, changed, rebuild));

        // What the shocker does
        var ops = Enum.GetValues<PiShockOp>();
        var op = new ComboBox
        {
            ItemsSource = ops.Select(o => o.DisplayName()).ToArray(),
            SelectedIndex = Array.IndexOf(ops, rule.Op),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        op.SelectionChanged += (_, _) =>
        {
            if (op.SelectedIndex < 0 || ops[op.SelectedIndex] == rule.Op) return;
            rule.Op = ops[op.SelectedIndex];
            changed();
            rebuild();
        };

        var targetNames = new List<string> { "Every shocker", "A random shocker" };
        targetNames.AddRange(Settings.Shockers.Select(s => s.Name));
        var target = new ComboBox
        {
            ItemsSource = targetNames,
            SelectedIndex = rule.Target switch
            {
                ShockerTarget.All => 0,
                ShockerTarget.Random => 1,
                _ => Settings.Shockers.FindIndex(s => s.Id == rule.ShockerId) is var i && i >= 0 ? i + 2 : 0
            },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        target.SelectionChanged += (_, _) =>
        {
            switch (target.SelectedIndex)
            {
                case 0: rule.Target = ShockerTarget.All; break;
                case 1: rule.Target = ShockerTarget.Random; break;
                case > 1:
                    rule.Target = ShockerTarget.One;
                    rule.ShockerId = Settings.Shockers[target.SelectedIndex - 2].Id;
                    break;
            }

            changed();
        };

        fields.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Children = { Ui.Field("Then", op), Ui.WithColumn(Ui.Field("On", target), 2) }
        });

        var intensityLabel = Ui.Text($"{rule.Intensity}%", "muted", 12);
        var intensity = new Slider { Minimum = 1, Maximum = 100, Value = rule.Intensity };
        intensity.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            rule.Intensity = (int)Math.Round(intensity.Value);
            intensityLabel.Text = $"{rule.Intensity}%";
            changed();
        };

        var durationLabel = Ui.Text($"{rule.DurationSeconds} s", "muted", 12);
        var duration = new Slider { Minimum = 1, Maximum = 15, Value = rule.DurationSeconds, TickFrequency = 1, IsSnapToTickEnabled = true };
        duration.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            rule.DurationSeconds = (int)Math.Round(duration.Value);
            durationLabel.Text = $"{rule.DurationSeconds} s";
            changed();
        };

        var intensityBlock = Ui.SliderBlock("Intensity (never above the shocker's maximum)", intensityLabel, intensity);
        intensityBlock.IsVisible = rule.Op != PiShockOp.Beep;
        fields.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Children = { intensityBlock, Ui.WithColumn(Ui.SliderBlock("Duration", durationLabel, duration), 2) }
        });

        var extras = new WrapPanel { ItemSpacing = 24, LineSpacing = 8 };
        var hasAmount = rule.Source == PiShockSource.Twitch ? rule.Twitch.Trigger.HasAmount() : rule.Fansly.Trigger.HasAmount();
        if (hasAmount)
        {
            var scale = new CheckBox { Content = "Bigger amounts last longer (up to 5×)", IsChecked = rule.ScaleWithAmount };
            scale.IsCheckedChanged += (_, _) =>
            {
                rule.ScaleWithAmount = scale.IsChecked == true;
                changed();
            };
            extras.Children.Add(scale);
        }

        if (rule.Op == PiShockOp.Shock)
        {
            var warn = new CheckBox { Content = "Beep first as a warning", IsChecked = rule.WarnFirst };
            warn.IsCheckedChanged += (_, _) =>
            {
                rule.WarnFirst = warn.IsChecked == true;
                changed();
            };
            extras.Children.Add(warn);
        }

        extras.Children.Add(rule.Source == PiShockSource.Twitch
            ? TwitchConditionEditor.Cooldown(rule.Twitch, changed)
            : FanslyConditionEditor.Cooldown(rule.Fansly, changed));
        fields.Children.Add(extras);

        var sourceOn = rule.Source == PiShockSource.Twitch ? PiShockPlugin.IsTwitchPluginOn : PiShockPlugin.IsFanslyPluginOn;
        if (!sourceOn)
        {
            fields.Children.Add(Ui.Text($"The {rule.Source} plugin is turned off in Settings, so this rule won't get any events.", "muted", 12));
        }

        return fields;
    }

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

    #endregion
}
