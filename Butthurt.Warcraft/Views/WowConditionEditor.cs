using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butthurt.Contracts.Views;
using Butthurt.Warcraft.Models;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Warcraft.Views;

/// <summary>
/// The "when this happens in WoW" bits, shared by WoW's toy rules and the PiShock rules on the WoW page:
/// the event picker, its cooldown, the grouped "Add rule" menu and the colored event tile.
/// </summary>
public static class WowConditionEditor
{
    /// <param name="eventChanged">Called after another event was picked, so the caller can update names and tiles.</param>
    public static StackPanel Create(WowCondition condition, Action changed, Action? eventChanged = null)
    {
        var events = WowEvents.Categories.SelectMany(WowEvents.InCategory).ToArray();
        var description = Ui.Text(WowEvents.Get(condition.Event).Description, "muted", 12);

        var picker = new ComboBox
        {
            ItemsSource = events.Select(e => $"{e.Category} · {e.Name}").ToArray(),
            SelectedIndex = Array.FindIndex(events, e => e.Type == condition.Event),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxDropDownHeight = 420
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0 || events[picker.SelectedIndex].Type == condition.Event) return;
            condition.Event = events[picker.SelectedIndex].Type;
            description.Text = events[picker.SelectedIndex].Description;
            changed();
            eventChanged?.Invoke();
        };

        return new StackPanel
        {
            Spacing = 6,
            Children = { Ui.Field("When this happens in WoW", picker), description }
        };
    }

    /// <summary>"Cooldown (seconds)" box, separate so callers can put it next to their own options.</summary>
    public static Control Cooldown(WowCondition condition, Action changed)
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

    /// <summary>An "Add rule" menu with a submenu per category (Combat, Progress, Group &amp; PvP, Social).</summary>
    public static MenuFlyout AddRuleMenu(Action<WowEventType> add)
    {
        var menu = new MenuFlyout();
        foreach (var category in WowEvents.Categories)
        {
            var group = new MenuItem { Header = category };
            foreach (var definition in WowEvents.InCategory(category))
            {
                var item = new MenuItem
                {
                    Header = definition.Name,
                    Icon = new PackIconLucide { Kind = definition.Icon, Width = 14, Height = 14, Foreground = new SolidColorBrush(Lighten(ColorOf(definition))) }
                };
                ToolTip.SetTip(item, definition.Description);
                item.Click += (_, _) => add(definition.Type);
                group.Items.Add(item);
            }

            menu.Items.Add(group);
        }

        return menu;
    }

    /// <summary>The event's icon on a tint of the color the addon flashes for it.</summary>
    public static Border Tile(WowEventType type)
    {
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 14, 0),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        SetTile(tile, type);
        return tile;
    }

    /// <summary>Repaints a tile made by <see cref="Tile"/> for another event.</summary>
    public static void SetTile(Border tile, WowEventType type)
    {
        var definition = WowEvents.Get(type);
        var color = ColorOf(definition);
        tile.Background = new SolidColorBrush(color, 0.15);
        tile.BorderBrush = new SolidColorBrush(color, 0.5);
        tile.Child = new PackIconLucide
        {
            Kind = definition.Icon, Width = 18, Height = 18,
            Foreground = new SolidColorBrush(Lighten(color)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static string Summary(WowCondition condition) =>
        WowEvents.Get(condition.Event).Name + (condition.CooldownSeconds > 0 ? $" · {condition.CooldownSeconds}s cooldown" : "");

    private static Color ColorOf(WowEventDefinition definition) => Color.FromRgb(definition.R, definition.G, definition.B);

    // Pure blue/red icons are hard to read on a dark background.
    private static Color Lighten(Color c) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * 0.45), (byte)(c.G + (255 - c.G) * 0.45), (byte)(c.B + (255 - c.B) * 0.45));
}
