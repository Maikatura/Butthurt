using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;

namespace Butthurt.Twitch.Views;

/// <summary>
/// The "when this happens on Twitch" fields: bits range, tiers, reward name, chat command, cooldown...
/// Only what makes sense for the condition's trigger is shown. Used by the Twitch page and by other
/// plugins that react to Twitch (VRChat).
/// </summary>
public static class TwitchConditionEditor
{
    /// <param name="rewardTitles">Suggestions for redeem rules, read when the reward box gets focus.</param>
    /// <param name="triggerChanged">When set, a "When" picker is shown and this is called after the trigger changed,
    /// so the caller can rebuild the editor with the right fields.</param>
    public static StackPanel Create(TwitchCondition condition, Action changed, Func<IReadOnlyList<string>> rewardTitles,
        Action? triggerChanged = null)
    {
        var fields = new StackPanel { Spacing = 12 };

        if (triggerChanged != null)
        {
            var triggers = Enum.GetValues<TwitchTrigger>();
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
                if (condition.Trigger == TwitchTrigger.ChatCommand && string.IsNullOrWhiteSpace(condition.Match)) condition.Match = "!grow";
                changed();
                triggerChanged();
            };
            fields.Children.Add(Ui.Field("When this happens on Twitch", picker));
        }

        switch (condition.Trigger)
        {
            case TwitchTrigger.Bits:
                fields.Children.Add(AmountRange(condition, changed, "Minimum bits", "Maximum bits (0 = no limit)"));
                break;

            case TwitchTrigger.Raid:
                fields.Children.Add(AmountRange(condition, changed, "Minimum viewers", "Maximum viewers (0 = no limit)"));
                break;

            case TwitchTrigger.GiftSub:
                fields.Children.Add(AmountRange(condition, changed, "Minimum gifted subs", "Maximum gifted subs (0 = no limit)"));
                fields.Children.Add(Ui.Field("Tiers", TierPicker(condition, changed)));
                break;

            case TwitchTrigger.Subscription:
                fields.Children.Add(Ui.Field("Tiers", TierPicker(condition, changed)));
                var resubs = new CheckBox { Content = "Also resubs (when someone shares their sub streak in chat)", IsChecked = condition.IncludeResubs };
                resubs.IsCheckedChanged += (_, _) =>
                {
                    condition.IncludeResubs = resubs.IsChecked == true;
                    changed();
                };
                fields.Children.Add(resubs);
                break;

            case TwitchTrigger.Redeem:
                var reward = new AutoCompleteBox
                {
                    Text = condition.Match,
                    Watermark = "Any reward",
                    FilterMode = AutoCompleteFilterMode.Contains,
                    MinimumPrefixLength = 0,
                    ItemsSource = rewardTitles()
                };
                reward.PropertyChanged += (_, e) =>
                {
                    if (e.Property != AutoCompleteBox.TextProperty) return;
                    condition.Match = reward.Text ?? "";
                    changed();
                };
                reward.GotFocus += (_, _) => reward.ItemsSource = rewardTitles();
                fields.Children.Add(Ui.Field("Reward name (exactly as on Twitch, leave empty for every redeem)", reward));
                fields.Children.Add(ManagedRewardOptions(condition, changed));
                break;

            case TwitchTrigger.ChatCommand:
                var command = new TextBox { Classes = { "field" }, Text = condition.Match, Watermark = "!buzz" };
                command.TextChanged += (_, _) =>
                {
                    condition.Match = command.Text ?? "";
                    changed();
                };
                fields.Children.Add(Ui.Field("Message starts with", command));

                var who = new ComboBox
                {
                    ItemsSource = new[] { "Everyone", "Subscribers and up", "VIPs and up", "Moderators only" },
                    SelectedIndex = (int)condition.WhoCanUse,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                who.SelectionChanged += (_, _) =>
                {
                    condition.WhoCanUse = (ChatterLevel)Math.Max(who.SelectedIndex, 0);
                    changed();
                };
                fields.Children.Add(Ui.Field("Who can use it", who));
                break;
        }

        return fields;
    }

    /// <summary>"Cooldown (seconds)" box, separate so callers can put it next to their own options.</summary>
    public static Control Cooldown(TwitchCondition condition, Action changed)
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

    /// <summary>"Let Butthurt create and manage this reward", its cost and how the sync is doing.</summary>
    private static Control ManagedRewardOptions(TwitchCondition condition, Action changed)
    {
        var status = Ui.Text("", "muted", 12);
        var cost = Ui.Number(condition.RewardCost, 1, 10_000_000, value =>
        {
            condition.RewardCost = value;
            changed();
        });
        cost.IsEnabled = condition.ManageReward;

        var manage = new CheckBox
        {
            Content = "Let Butthurt create this reward on Twitch, keep it in sync, hide it while the rule is off and delete it with the rule",
            IsChecked = condition.ManageReward
        };

        void UpdateStatus()
        {
            var title = condition.Match.Trim();
            var reward = TwitchPlugin.Settings.ManagedRewards
                .FirstOrDefault(r => string.Equals(r.Title, title, StringComparison.OrdinalIgnoreCase));

            status.Text = !condition.ManageReward ? "The reward has to exist on Twitch already (made by you or on the Twitch page)."
                : title.Length == 0 ? "Give the reward a name above."
                : !TwitchService.IsLoggedIn ? "It's created as soon as you're logged in to Twitch."
                : !TwitchService.CanManageRedeems ? "Log out of Twitch and in again so Butthurt may manage rewards."
                : TwitchService.SyncErrorFor(title) is { } error ? $"Twitch said: {error}"
                : reward == null ? (TwitchService.State == TwitchState.Connected ? "Creating it on Twitch…" : "It's created once Twitch is connected.")
                : reward.Cost != condition.RewardCost ? "Updating it on Twitch…"
                : reward.IsEnabled ? $"Live on your channel for {reward.Cost:N0} points. Redemptions are completed or refunded for you."
                : "On Twitch but hidden from viewers while the rule (or its plugin) is off.";
        }

        manage.IsCheckedChanged += (_, _) =>
        {
            condition.ManageReward = manage.IsChecked == true;
            cost.IsEnabled = condition.ManageReward;
            changed();
            UpdateStatus();
        };

        // Follow the sync and the reward name while the editor is open.
        EventHandler refresh = (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        status.AttachedToVisualTree += (_, _) =>
        {
            TwitchService.RewardsChanged += refresh;
            TwitchService.StateChanged += refresh;
            SettingsService.Changed += refresh;
            UpdateStatus();
        };
        status.DetachedFromVisualTree += (_, _) =>
        {
            TwitchService.RewardsChanged -= refresh;
            TwitchService.StateChanged -= refresh;
            SettingsService.Changed -= refresh;
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                manage,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Margin = new Thickness(28, 0, 0, 0),
                    Children = { WithCenter(Ui.Text("Cost in channel points", "muted", 12)), cost }
                },
                status
            }
        };
    }

    private static TextBlock WithCenter(TextBlock text)
    {
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }

    private static Control AmountRange(TwitchCondition condition, Action changed, string minLabel, string maxLabel)
    {
        var min = Ui.Number(condition.MinAmount, 1, 1_000_000, value =>
        {
            condition.MinAmount = value;
            changed();
        });
        var max = Ui.Number(condition.MaxAmount, 0, 1_000_000, value =>
        {
            condition.MaxAmount = value;
            changed();
        });

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Children = { Ui.Field(minLabel, min), Ui.WithColumn(Ui.Field(maxLabel, max), 2) }
        };
    }

    private static Control TierPicker(TwitchCondition condition, Action changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        foreach (var (tier, label) in new[] { (1, "Tier 1 (and Prime)"), (2, "Tier 2"), (3, "Tier 3") })
        {
            var check = new CheckBox { Content = label, IsChecked = condition.Tiers.Contains(tier) };
            check.IsCheckedChanged += (_, _) =>
            {
                condition.Tiers.Remove(tier);
                if (check.IsChecked == true) condition.Tiers.Add(tier);
                condition.Tiers.Sort();
                changed();
            };
            panel.Children.Add(check);
        }

        return panel;
    }
}
