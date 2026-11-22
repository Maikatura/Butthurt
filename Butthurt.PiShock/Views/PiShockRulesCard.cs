using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Fansly.Models;
using Butthurt.Fansly.Views;
using Butthurt.PiShock.Models;
using Butthurt.PiShock.Services;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.Twitch.Views;
using Butthurt.VRChat.Models;
using Butthurt.VRChat.Views;
using Butthurt.Warcraft.Models;
using Butthurt.Warcraft.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.PiShock.Views;

/// <summary>
/// "PiShock rules" for one platform. Shown on the Twitch or Fansly page (see PageSections), so the PiShock page
/// itself only holds the setup: account, safety and shockers.
/// </summary>
public class PiShockRulesCard : Border
{
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FBBF24"));
    private static readonly IBrush AmberSoft = new SolidColorBrush(Color.Parse("#26F59E0B"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush RedSoft = new SolidColorBrush(Color.Parse("#26EF4444"));

    private readonly PiShockSource _source;
    private readonly HashSet<string> _expandedRules = new();
    private readonly StackPanel _ruleList = new() { Spacing = 8 };
    private readonly TextBlock _noRules;
    private readonly Border _setupHint;
    private readonly TextBlock _setupHintText;
    private List<string> _rewardTitles = new();

    // Styles match exact types, so without this the theme's Border.card style (frame, padding) would skip this card.
    protected override Type StyleKeyOverride => typeof(Border);

    private static PiShockSettings Settings => PiShockPlugin.Settings;
    private static List<PiShockRule> AllRules => Settings.Rules!;
    private IEnumerable<PiShockRule> Rules => AllRules.Where(r => r.Source == _source);

    public PiShockRulesCard(PiShockSource source)
    {
        _source = source;
        Classes.Add("card");

        var add = new Button
        {
            Classes = { "bt", "primary" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Flyout = CreateAddRuleMenu(),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new PackIconLucide { Kind = PackIconLucideKind.Plus, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Add rule", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };

        var where = source switch
        {
            PiShockSource.Warcraft => "in WoW",
            PiShockSource.VRChat => "on your avatar in VRChat",
            _ => $"on {source}"
        };
        _noRules = Ui.Text(source switch
        {
            PiShockSource.Warcraft => "No PiShock rules yet. Add one to decide what happens to your shocker in game, e.g. when you die.",
            PiShockSource.VRChat => "No PiShock rules yet. Add one, then pick a contact (like a boop) or toggle from your avatar.",
            _ => $"No PiShock rules yet. Add one to decide what {source} can do to your shocker."
        }, "muted");

        _setupHintText = Ui.Text("", "muted", 12);
        _setupHintText.VerticalAlignment = VerticalAlignment.Center;
        var openSetup = new Button { Classes = { "bt", "small" }, Content = "Set up PiShock", VerticalAlignment = VerticalAlignment.Center };
        openSetup.Click += (_, _) => NavigationService.NavigateToPlugin(PiShockPlugin.PluginId);
        _setupHint = new Border
        {
            Classes = { "row" },
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { _setupHintText, Ui.WithColumn(openSetup, 1) }
            }
        };

        Child = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 2,
                            Children =
                            {
                                Ui.Text("PiShock rules", "h2"),
                                Ui.Text($"When something happens {where}, every matching rule shocks, vibrates or beeps your PiShock. " +
                                        "Limits and safety switches are on the PiShock page and always win.", "muted")
                            }
                        },
                        Ui.WithColumn(add, 1)
                    }
                },
                _setupHint,
                _noRules,
                _ruleList
            }
        };

        // Follow the PiShock setup (key saved, shockers added…) while the card is on screen.
        EventHandler refresh = (_, _) => Dispatcher.UIThread.Post(UpdateSetupHint);
        AttachedToVisualTree += async (_, _) =>
        {
            PiShockService.StatusChanged += refresh;
            UpdateSetupHint();
            BuildRuleList();
            if (_source == PiShockSource.Twitch && TwitchService.IsLoggedIn) _rewardTitles = await TwitchService.GetRewardTitles();
        };
        DetachedFromVisualTree += (_, _) => PiShockService.StatusChanged -= refresh;
    }

    private void UpdateSetupHint()
    {
        _setupHintText.Text = !PiShockService.HasLogin ? "PiShock isn't set up yet: add your API key on the PiShock page."
            : !PiShockService.HasShockers ? "No shockers yet: press Find my shockers on the PiShock page."
            : Settings.Paused ? "PiShock is paused, so these rules do nothing until you unpause it on the PiShock page."
            : "";
        _setupHint.IsVisible = _setupHintText.Text.Length > 0;
    }

    private MenuFlyout CreateAddRuleMenu()
    {
        if (_source == PiShockSource.Warcraft) return WowConditionEditor.AddRuleMenu(type => AddRule(wowEvent: type));

        if (_source == PiShockSource.VRChat)
        {
            var vrMenu = new MenuFlyout();
            var parameter = new MenuItem
            {
                Header = "Avatar parameter (contact, toggle…)",
                Icon = new PackIconLucide { Kind = PackIconLucideKind.Hand, Width = 14, Height = 14 }
            };
            parameter.Click += (_, _) => AddRule();
            vrMenu.Items.Add(parameter);
            return vrMenu;
        }

        var menu = new MenuFlyout();
        if (_source == PiShockSource.Twitch)
        {
            foreach (var trigger in Enum.GetValues<TwitchTrigger>())
            {
                var item = new MenuItem { Header = trigger.DisplayName(), Icon = new PackIconLucide { Kind = trigger.Icon(), Width = 14, Height = 14 } };
                item.Click += (_, _) => AddRule(twitchTrigger: trigger);
                menu.Items.Add(item);
            }
        }
        else
        {
            foreach (var trigger in Enum.GetValues<FanslyTrigger>())
            {
                var item = new MenuItem { Header = trigger.DisplayName(), Icon = new PackIconLucide { Kind = trigger.Icon(), Width = 14, Height = 14 } };
                item.Click += (_, _) => AddRule(fanslyTrigger: trigger);
                menu.Items.Add(item);
            }
        }

        return menu;
    }

    private void AddRule(TwitchTrigger twitchTrigger = TwitchTrigger.Bits, FanslyTrigger fanslyTrigger = FanslyTrigger.Tip,
        WowEventType wowEvent = WowEventType.Death)
    {
        var twitch = _source == PiShockSource.Twitch;
        var chat = _source switch
        {
            PiShockSource.Twitch => twitchTrigger == TwitchTrigger.ChatCommand,
            PiShockSource.Fansly => fanslyTrigger == FanslyTrigger.ChatCommand,
            _ => false
        };
        var rule = new PiShockRule
        {
            Source = _source,
            Name = _source switch
            {
                PiShockSource.Twitch => twitchTrigger.DisplayName(),
                PiShockSource.Fansly => fanslyTrigger.DisplayName(),
                PiShockSource.VRChat => "",
                _ => WowEvents.Get(wowEvent).Name
            },
            Warcraft = new WowCondition { Event = wowEvent },
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

        AllRules.Add(rule);
        _expandedRules.Add(rule.Id);
        SettingsService.Save();
        BuildRuleList();
    }

    private void BuildRuleList()
    {
        _ruleList.Children.Clear();
        foreach (var rule in Rules) _ruleList.Children.Add(CreateRuleRow(rule));
        _noRules.IsVisible = _ruleList.Children.Count == 0;
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
            if (!PiShockService.HasLogin) await NotificationService.ShowAsync("Set up PiShock on the PiShock page first.");
            else if (!PiShockService.Test(rule)) await NotificationService.ShowAsync("Nothing was sent, check the activity log.", 4000);
        };

        var expanded = _expandedRules.Contains(rule.Id);
        var edit = Ui.IconButton(expanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil, "Edit");
        var delete = Ui.IconButton(PackIconLucideKind.Trash2, "Delete");

        // The editor is rebuilt when the trigger or action changes, so only the right fields show.
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
            // Two clicks, so a misclick doesn't throw away a carefully tuned rule.
            if (!confirmDelete)
            {
                confirmDelete = true;
                delete.Content = Ui.Text("Delete?", "body", 12);
                delete.Classes.Add("danger");
                return;
            }

            AllRules.Remove(rule);
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

        // When: this page's platform only.
        fields.Children.Add(rule.Source switch
        {
            PiShockSource.Twitch => TwitchConditionEditor.Create(rule.Twitch, changed, () => _rewardTitles, rebuild),
            PiShockSource.Fansly => FanslyConditionEditor.Create(rule.Fansly, changed, rebuild),
            PiShockSource.VRChat => VRChatTriggerEditor.Create(rule.VRChat, changed),
            _ => WowConditionEditor.Create(rule.Warcraft, changed, rebuild)
        });

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
        var hasAmount = rule.Source switch
        {
            PiShockSource.Twitch => rule.Twitch.Trigger.HasAmount(),
            PiShockSource.Fansly => rule.Fansly.Trigger.HasAmount(),
            _ => false
        };
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

        // VRChat's trigger editor has its cooldown built in.
        if (rule.Source != PiShockSource.VRChat)
        {
            extras.Children.Add(rule.Source switch
            {
                PiShockSource.Twitch => TwitchConditionEditor.Cooldown(rule.Twitch, changed),
                PiShockSource.Fansly => FanslyConditionEditor.Cooldown(rule.Fansly, changed),
                _ => WowConditionEditor.Cooldown(rule.Warcraft, changed)
            });
        }
        fields.Children.Add(extras);

        return fields;
    }
}
