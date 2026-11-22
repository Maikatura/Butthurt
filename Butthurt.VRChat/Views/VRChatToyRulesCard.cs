using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.VRChat.Models;
using Butthurt.VRChat.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.VRChat.Views;

/// <summary>
/// "Toy rules" on the VRChat page: avatar parameters (headpats, boops, toggles…) that make your toys buzz.
/// Only shown with the Toys plugin on.
/// </summary>
public class VRChatToyRulesCard : Border
{
    private readonly HashSet<string> _expandedRules = new();
    private readonly StackPanel _ruleList = new() { Spacing = 8 };
    private readonly TextBlock _noRules;

    // Styles match exact types, so without this the theme's Border.card style would skip this card.
    protected override Type StyleKeyOverride => typeof(Border);

    private static List<VRChatToyRule> Rules => VRChatPlugin.Settings.ToyRules;

    public VRChatToyRulesCard()
    {
        Classes.Add("card");

        var add = new Button
        {
            Classes = { "bt", "primary" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
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
        add.Click += (_, _) => AddRule();

        _noRules = Ui.Text("No toy rules yet. Add one, then pick a contact (like a headpat) or toggle from your avatar.", "muted");

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
                                Ui.Text("Toy rules", "h2"),
                                Ui.Text("When something happens on your avatar in VRChat, buzz your toys. Contacts (headpats, boops…), " +
                                        "toggles and sliders all come in as avatar parameters over OSC.", "muted")
                            }
                        },
                        Ui.WithColumn(add, 1)
                    }
                },
                _noRules,
                _ruleList
            }
        };

        AttachedToVisualTree += (_, _) => BuildRuleList();
    }

    private void AddRule()
    {
        var rule = new VRChatToyRule { Name = "", Intensity = 0.5, DurationMs = 800 };
        Rules.Add(rule);
        _expandedRules.Add(rule.Id);
        SettingsService.Save();
        BuildRuleList();
    }

    private void BuildRuleList()
    {
        _ruleList.Children.Clear();
        foreach (var rule in Rules) _ruleList.Children.Add(CreateRuleRow(rule));
        _noRules.IsVisible = Rules.Count == 0;
    }

    private static string Summary(VRChatToyRule rule) =>
        $"{VRChatTriggers.ConditionSummary(rule)} → {rule.Intensity * 100:0}% for {Ui.FormatDuration(rule.DurationMs)}" +
        (rule.WhileOn ? " · keeps going while on" : "");

    private Control CreateRuleRow(VRChatToyRule rule)
    {
        var nameText = Ui.Text(VRChatTriggers.Label(rule), "h3");
        var summaryText = Ui.Text(Summary(rule), "muted", 12);

        void Changed()
        {
            nameText.Text = VRChatTriggers.Label(rule);
            summaryText.Text = Summary(rule);
            SettingsService.SaveSoon();
        }

        var toggle = new ToggleSwitch { IsChecked = rule.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };

        var test = new Button { Classes = { "bt", "small" }, Content = "Test", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += async (_, _) =>
        {
            if (!ButthurtService.IsConnected) await NotificationService.ShowAsync("Connect to Intiface Central to feel the test.");
            VRChatTriggers.Test(rule);
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
                    Classes = { "iconbox" },
                    Margin = new Thickness(0, 0, 14, 0),
                    Child = new PackIconLucide
                    {
                        Kind = PackIconLucideKind.Hand, Width = 18, Height = 18,
                        Foreground = Ui.Brush("TextBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                },
                Ui.WithColumn(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { nameText, summaryText } }, 1),
                Ui.WithColumn(toggle, 2),
                Ui.WithColumn(test, 3),
                Ui.WithColumn(edit, 4),
                Ui.WithColumn(delete, 5)
            }
        });
        body.Children.Add(editor);

        return row;
    }

    private static Control CreateEditor(VRChatToyRule rule, Action changed)
    {
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(54, 4, 0, 4) };

        var name = new TextBox { Classes = { "field" }, Text = rule.Name, Watermark = "Name (the parameter's name when empty)" };
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text ?? "";
            changed();
        };
        fields.Children.Add(Ui.Field("Name", name));
        fields.Children.Add(VRChatTriggerEditor.Create(rule, changed));

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
        var duration = new Slider { Minimum = 100, Maximum = 5000, Value = rule.DurationMs };
        duration.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            rule.DurationMs = (int)(Math.Round(duration.Value / 50) * 50);
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

        var whileOn = new CheckBox { Content = "Keep buzzing while it stays on (e.g. for as long as you're headpatted)", IsChecked = rule.WhileOn };
        whileOn.IsCheckedChanged += (_, _) =>
        {
            rule.WhileOn = whileOn.IsChecked == true;
            changed();
        };
        fields.Children.Add(whileOn);

        return fields;
    }
}
