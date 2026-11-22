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
using Butthurt.Fansly.Services;
using Butthurt.Fansly.Views;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.Twitch.Views;
using Butthurt.VRChat.Models;
using Butthurt.VRChat.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.VRChat.Views;

/// <summary>
/// "VRChat rules" for one platform: what Twitch or Fansly can do in VRChat (grow, switch avatar, set a parameter),
/// with its announcement. Shown on the Twitch and Fansly pages (see PageSections); the VRChat page is the setup.
/// </summary>
public class VRChatRulesCard : Border
{
    private static readonly IBrush Lilac = new SolidColorBrush(Color.Parse("#FAFAFA"));

    private readonly VRChatSource _source;
    private readonly HashSet<string> _expandedRules = new();
    private readonly StackPanel _ruleList = new() { Spacing = 8 };
    private readonly TextBlock _noRules;
    private readonly Border _hint;
    private readonly TextBlock _hintText;
    private List<string> _rewardTitles = new();

    // Styles match exact types, so without this the theme's Border.card style would skip this card.
    protected override Type StyleKeyOverride => typeof(Border);

    private static VRChatSettings Settings => VRChatPlugin.Settings;
    private static List<VRChatRule> AllRules => Settings.Rules!;
    private IEnumerable<VRChatRule> Rules => AllRules.Where(r => r.Source == _source);

    public VRChatRulesCard(VRChatSource source)
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

        _noRules = Ui.Text($"No VRChat rules yet. Add one to let {source} grow you, switch your avatar or change a parameter.", "muted");

        _hintText = Ui.Text("", "muted", 12);
        _hintText.VerticalAlignment = VerticalAlignment.Center;
        var openVrChat = new Button { Classes = { "bt", "small" }, Content = "VRChat setup", VerticalAlignment = VerticalAlignment.Center };
        openVrChat.Click += (_, _) => NavigationService.NavigateToPlugin(VRChatPlugin.PluginId);
        _hint = new Border
        {
            Classes = { "row" },
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _hintText, Ui.WithColumn(openVrChat, 1) } }
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
                                Ui.Text("VRChat rules", "h2"),
                                Ui.Text($"When something happens on {source}, every matching rule grows or shrinks you, switches your avatar " +
                                        "or changes an avatar parameter, and can announce it on the stream overlay.", "muted")
                            }
                        },
                        Ui.WithColumn(add, 1)
                    }
                },
                _hint,
                _noRules,
                _ruleList
            }
        };

        EventHandler refresh = (_, _) => Dispatcher.UIThread.Post(UpdateHint);
        AttachedToVisualTree += async (_, _) =>
        {
            VRChatService.StatusChanged += refresh;
            UpdateHint();
            BuildRuleList();
            if (_source == VRChatSource.Twitch && TwitchService.IsLoggedIn) _rewardTitles = await TwitchService.GetRewardTitles();
        };
        DetachedFromVisualTree += (_, _) => VRChatService.StatusChanged -= refresh;
    }

    private void UpdateHint()
    {
        _hintText.Text = !VRChatService.IsRunning ? "Butthurt isn't talking to VRChat: press Start on the VRChat page."
            : !VRChatService.IsVRChatRunning ? "VRChat isn't running (or OSC is off), these rules wait for it."
            : "";
        _hint.IsVisible = _hintText.Text.Length > 0;
    }

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
            Source = _source,
            When = new TwitchCondition { Trigger = TwitchTrigger.Bits, MinAmount = action == VRChatAction.Grow ? 20 : 100 },
            Fansly = new FanslyCondition { Trigger = FanslyTrigger.Tip, MinTip = action == VRChatAction.Grow ? 2 : 5 }
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

        var when = rule.Source == VRChatSource.Fansly ? FanslyRules.ConditionSummary(rule.Fansly) : TwitchRules.ConditionSummary(rule.When);
        return $"{when} → {action}";
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

        var toggle = new ToggleSwitch { IsChecked = rule.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
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

            AllRules.Remove(rule);
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

    /// <summary>Name, the Twitch or Fansly condition and the VRChat action. Rebuilt when the trigger changes.</summary>
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

        fields.Children.Add(rule.Source == VRChatSource.Fansly
            ? FanslyConditionEditor.Create(rule.Fansly, changed, rebuild)
            : TwitchConditionEditor.Create(rule.When, changed, () => _rewardTitles, rebuild));

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

        fields.Children.Add(rule.Source == VRChatSource.Fansly
            ? FanslyConditionEditor.Cooldown(rule.Fansly, changed)
            : TwitchConditionEditor.Cooldown(rule.When, changed));
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
    internal static IReadOnlyList<AvatarInfo> AvatarChoices()
    {
        var all = AvatarLibrary.All;
        var current = all.FirstOrDefault(a => a.Id == VRChatService.CurrentAvatarId);
        return current == null ? all : all.Where(a => a != current).Prepend(current).ToList();
    }

    /// <summary>A button that opens a searchable list and reports the picked item.</summary>
    internal static Button PickButton<T>(string label, Func<IReadOnlyList<T>> items, Func<T, string> title, Func<T, string> subtitle,
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

    /// <summary>"Announce this rule", the message with {placeholders} and a live preview.</summary>
    private static Control AnnouncementEditor(VRChatRule rule, Action changed)
    {
        var preview = Ui.Text("", "body", 13);
        preview.Foreground = new SolidColorBrush(Color.Parse("#E4E4E7"));

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
            var size = rule.Action == VRChatAction.Grow ? VRChatService.CurrentMultiplier * rule.Multiplier : VRChatService.CurrentMultiplier;
            preview.Text = rule.Source == VRChatSource.Fansly
                ? Announcer.Text(rule, FanslyRules.SampleFor(rule.Fansly) with { UserName = "TestUser" }, size, VRChatService.CurrentAvatarId)
                : Announcer.Text(rule, TwitchRules.SampleFor(rule.When) with { UserName = "TestUser" }, size, VRChatService.CurrentAvatarId);
        }

        template.TextChanged += (_, _) =>
        {
            rule.AnnouncementTemplate = template.Text ?? "";
            changed();
            UpdatePreview();
        };

        var announce = new CheckBox
        {
            Content = rule.Source == VRChatSource.Fansly
                ? "Announce this rule on the stream overlay"
                : "Announce this rule on the overlay (and in chat if that's turned on)",
            IsChecked = rule.Announce
        };
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
}
