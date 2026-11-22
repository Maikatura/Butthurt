using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Butthurt.Contracts.Views;
using Butthurt.VRChat.Models;
using Butthurt.VRChat.Services;

namespace Butthurt.VRChat.Views;

/// <summary>
/// The "when this avatar parameter goes on" fields, shared by the toy rules on the VRChat page and PiShock's VRChat
/// rules: the parameter (typed, picked from the avatar, or clicked from what changed lately), its live value,
/// the threshold and the cooldown.
/// </summary>
public static class VRChatTriggerEditor
{
    public static StackPanel Create(VRChatParameterCondition condition, Action changed)
    {
        var fields = new StackPanel { Spacing = 10 };

        var name = new AutoCompleteBox
        {
            Text = condition.Parameter,
            Watermark = "Parameter name, e.g. Headpat",
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            ItemsSource = Suggestions()
        };
        name.GotFocus += (_, _) => name.ItemsSource = Suggestions();

        var live = Ui.Text("", "muted", 12);
        live.VerticalAlignment = VerticalAlignment.Center;
        live.Margin = new Thickness(12, 0, 0, 0);

        void UpdateLive()
        {
            var parameter = condition.Parameter.Trim();
            live.Text = parameter.Length == 0 ? "" : $"Now: {VRChatTriggers.ValueOf(parameter):0.##}";
        }

        name.PropertyChanged += (_, e) =>
        {
            if (e.Property != AutoCompleteBox.TextProperty) return;
            condition.Parameter = name.Text?.Trim() ?? "";
            UpdateLive();
            changed();
        };

        fields.Children.Add(Ui.Field("When this avatar parameter goes on",
            new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { name, Ui.WithColumn(live, 1) } }));

        // "Touch it, then click it": whatever changed in VRChat lately, as chips.
        var recent = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var recentHint = Ui.Text("Recently changed in VRChat (touch the contact or flip the toggle in game, then click it):", "muted", 12);
        var recentBlock = new StackPanel { Spacing = 6, Children = { recentHint, recent } };

        void UpdateRecent()
        {
            recent.Children.Clear();
            foreach (var parameter in VRChatTriggers.RecentlyChanged)
            {
                var chip = new Button { Classes = { "bt", "small" }, Content = parameter, Padding = new Thickness(8, 3), FontSize = 11 };
                chip.Click += (_, _) => name.Text = parameter;
                recent.Children.Add(chip);
            }

            recentBlock.IsVisible = recent.Children.Count > 0;
        }

        fields.Children.Add(recentBlock);

        var threshold = new NumericUpDown
        {
            Value = (decimal)condition.Threshold,
            Minimum = -1,
            Maximum = 255,
            Increment = 0.05m,
            FormatString = "0.##",
            MinWidth = 140,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        threshold.ValueChanged += (_, e) =>
        {
            if (e.NewValue is not { } value) return;
            condition.Threshold = (double)value;
            changed();
        };

        var cooldown = Ui.Number(condition.CooldownSeconds, 0, 3600, value =>
        {
            condition.CooldownSeconds = value;
            changed();
        });

        fields.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Children =
            {
                Ui.Field("Goes on at (0.5 for on/off parameters, e.g. 0.8 for a proximity contact)", threshold),
                Ui.WithColumn(Ui.Field("Cooldown (seconds)", cooldown), 2)
            }
        });

        // Follow VRChat while the editor is open.
        EventHandler<ParameterChange> onChange = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            UpdateLive();
            UpdateRecent();
        });
        fields.AttachedToVisualTree += (_, _) =>
        {
            VRChatTriggers.ParameterChanged += onChange;
            UpdateLive();
            UpdateRecent();
        };
        fields.DetachedFromVisualTree += (_, _) => VRChatTriggers.ParameterChanged -= onChange;

        return fields;
    }

    /// <summary>Every parameter of the avatar you're wearing (contacts included), plus anything VRChat sent lately.</summary>
    private static List<string> Suggestions()
    {
        var names = new List<string>();
        if (AvatarLibrary.Get(VRChatService.CurrentAvatarId) is { } avatar) names.AddRange(avatar.Parameters.Select(p => p.Name));
        names.AddRange(VRChatService.SeenParameters);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
