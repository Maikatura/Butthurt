using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Butthurt.Contracts.Views;
using Butthurt.Fansly.Models;

namespace Butthurt.Fansly.Views;

/// <summary>
/// The "when this happens on Fansly" fields: tip range, tiers, chat command, cooldown. Only what makes sense for
/// the condition's trigger is shown. Used by the Fansly page and by other plugins that react to Fansly (PiShock).
/// </summary>
public static class FanslyConditionEditor
{
    /// <param name="triggerChanged">When set, a "When" picker is shown and this is called after the trigger changed,
    /// so the caller can rebuild the editor with the right fields.</param>
    public static StackPanel Create(FanslyCondition condition, Action changed, Action? triggerChanged = null)
    {
        var fields = new StackPanel { Spacing = 12 };

        if (triggerChanged != null)
        {
            var triggers = Enum.GetValues<FanslyTrigger>();
            var picker = new ComboBox
            {
                ItemsSource = Array.ConvertAll(triggers, t => t.DisplayName()),
                SelectedIndex = Array.IndexOf(triggers, condition.Trigger),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0 || triggers[picker.SelectedIndex] == condition.Trigger) return;
                condition.Trigger = triggers[picker.SelectedIndex];
                if (condition.Trigger == FanslyTrigger.ChatCommand && string.IsNullOrWhiteSpace(condition.Match)) condition.Match = "!buzz";
                changed();
                triggerChanged();
            };
            fields.Children.Add(Ui.Field("When this happens on Fansly", picker));
        }

        switch (condition.Trigger)
        {
            case FanslyTrigger.Tip:
                var min = Dollars(condition.MinTip, 0.01, value =>
                {
                    condition.MinTip = value;
                    changed();
                });
                var max = Dollars(condition.MaxTip, 0, value =>
                {
                    condition.MaxTip = value;
                    changed();
                });
                fields.Children.Add(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,24,*"),
                    Children = { Ui.Field("Minimum tip ($)", min), Ui.WithColumn(Ui.Field("Maximum tip ($, 0 = no limit)", max), 2) }
                });
                break;

            case FanslyTrigger.Subscription:
                fields.Children.Add(Ui.Field("Tiers (none ticked = every tier)", TierPicker(condition, changed)));
                var resubs = new CheckBox { Content = "Also renewals (subscribers on a streak)", IsChecked = condition.IncludeResubs };
                resubs.IsCheckedChanged += (_, _) =>
                {
                    condition.IncludeResubs = resubs.IsChecked == true;
                    changed();
                };
                fields.Children.Add(resubs);
                break;

            case FanslyTrigger.ChatCommand:
                var command = new TextBox { Classes = { "field" }, Text = condition.Match, Watermark = "!buzz" };
                command.TextChanged += (_, _) =>
                {
                    condition.Match = command.Text ?? "";
                    changed();
                };
                fields.Children.Add(Ui.Field("Message starts with", command));

                var who = new ComboBox
                {
                    ItemsSource = new[] { "Everyone", "Followers and up", "Subscribers and up", "Only you" },
                    SelectedIndex = (int)condition.WhoCanUse,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                who.SelectionChanged += (_, _) =>
                {
                    condition.WhoCanUse = (FanslyChatterLevel)Math.Max(who.SelectedIndex, 0);
                    changed();
                };
                fields.Children.Add(Ui.Field("Who can use it", who));
                break;
        }

        return fields;
    }

    /// <summary>"Cooldown (seconds)" box, separate so callers can put it next to their own options.</summary>
    public static Control Cooldown(FanslyCondition condition, Action changed)
    {
        var cooldown = Ui.Number(condition.CooldownSeconds, 0, 3600, value =>
        {
            condition.CooldownSeconds = value;
            changed();
        });

        var label = Ui.Text("Cooldown (seconds)", "muted", 12);
        label.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { label, cooldown }
        };
    }

    private static NumericUpDown Dollars(double value, double minimum, Action<double> changed)
    {
        var box = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)minimum,
            Maximum = 100_000,
            Increment = 1,
            FormatString = "0.00",
            MinWidth = 140,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } newValue) changed((double)newValue);
        };
        return box;
    }

    private static Control TierPicker(FanslyCondition condition, Action changed)
    {
        // The creator's tiers from the last lookup, plus any a rule still names (e.g. a renamed tier).
        var names = FanslyPlugin.Settings.KnownTiers
            .Concat(condition.Tiers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return Ui.Text("Set your Fansly username at the top of the Fansly page to pick tiers. Until then every tier counts.", "muted", 12);
        }

        var panel = new WrapPanel { ItemSpacing = 20, LineSpacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var name in names)
        {
            var check = new CheckBox
            {
                Content = name,
                IsChecked = condition.Tiers.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))
            };
            check.IsCheckedChanged += (_, _) =>
            {
                condition.Tiers.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
                if (check.IsChecked == true) condition.Tiers.Add(name);
                changed();
            };
            panel.Children.Add(check);
        }

        return panel;
    }
}
