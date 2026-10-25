using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Butthurt.Contracts.Services;

namespace Butthurt.Contracts.Views;

/// <summary>Small helpers for building UI from code with the theme in Styles/Theme.axaml.</summary>
public static class Ui
{
    public static IBrush Brush(string key) =>
        Application.Current?.FindResource(key) as IBrush ?? Brushes.White;

    public static IBrush LevelBrush(LogLevel level) => level switch
    {
        LogLevel.Success => Brush("SuccessBrush"),
        LogLevel.Warning => Brush("WarningBrush"),
        LogLevel.Error => Brush("DangerBrush"),
        LogLevel.Event => Brush("Accent2Brush"),
        _ => Brush("DimBrush")
    };

    public static TextBlock Text(string text, string classes, double? fontSize = null)
    {
        var block = new TextBlock { Text = text };
        foreach (var c in classes.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)) block.Classes.Add(c);
        if (fontSize.HasValue) block.FontSize = fontSize.Value;
        return block;
    }

    public static Control ActivityRow(LogEntry entry) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
        Margin = new Thickness(0, 4),
        Children =
        {
            new Ellipse
            {
                Width = 7, Height = 7, Fill = LevelBrush(entry.Level),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0)
            },
            WithColumn(new TextBlock
            {
                Text = entry.Time.ToString("HH:mm:ss"),
                FontSize = 12,
                Foreground = Brush("DimBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            }, 1),
            WithColumn(new TextBlock
            {
                Text = entry.Message,
                FontSize = 13,
                Foreground = Brush("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            }, 2)
        }
    };

    /// <summary>A card with a heading, an optional subtitle and content below.</summary>
    public static Border Card(string title, string? subtitle, params Control[] content)
    {
        var stack = new StackPanel { Spacing = 14 };
        var header = new StackPanel { Spacing = 4, Children = { Text(title, "h2") } };
        if (subtitle != null) header.Children.Add(Text(subtitle, "muted"));
        stack.Children.Add(header);
        foreach (var control in content) stack.Children.Add(control);

        return new Border { Classes = { "card" }, Child = stack };
    }

    /// <summary>A step with a round gradient number in front, used by the guides.</summary>
    public static Control NumberedStep(int number, string title, string body) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        Children =
        {
            new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                Background = Brush("AccentGradient"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 14, 0),
                Child = new TextBlock
                {
                    Text = number.ToString(), FontWeight = FontWeight.Bold, FontSize = 13, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            },
            WithColumn(new StackPanel
            {
                Spacing = 2,
                Children = { Text(title, "h3"), Text(body, "muted") }
            }, 1)
        }
    };

    public static Control CodeBlock(string code) => new Border
    {
        Classes = { "codeblock" },
        Margin = new Thickness(42, 0, 0, 0),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = Text(code, "code")
        }
    };

    /// <summary>A bordered row with a bold title and a muted line, e.g. a troubleshooting tip.</summary>
    public static Control InfoRow(string title, string body) => new Border
    {
        Classes = { "row" },
        Child = new StackPanel { Spacing = 2, Children = { Text(title, "h3"), Text(body, "muted", 12) } }
    };

    /// <summary>A muted label above an input.</summary>
    public static Control Field(string label, Control input) => new StackPanel
    {
        Spacing = 6,
        Children = { Text(label, "muted", 12), input }
    };

    /// <summary>A whole-number box that reports every change.</summary>
    public static NumericUpDown Number(int value, int minimum, int maximum, System.Action<int> changed)
    {
        var box = new NumericUpDown
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            Increment = 1,
            FormatString = "0",
            MinWidth = 140,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } newValue) changed((int)newValue);
        };
        return box;
    }

    /// <summary>A slider with its title on the left and its current value on the right.</summary>
    public static Control SliderBlock(string title, TextBlock valueLabel, Slider slider)
    {
        Grid.SetColumn(valueLabel, 1);
        return new StackPanel
        {
            Children =
            {
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Text(title, "muted", 12), valueLabel } },
                slider
            }
        };
    }

    public static string FormatDuration(int ms) => ms < 1000 ? $"{ms} ms" : $"{ms / 1000.0:0.#} s";

    /// <summary>Small ghost button with just an icon (tooltip and screen reader name included).</summary>
    public static Button IconButton(IconPacks.Avalonia.Lucide.PackIconLucideKind icon, string tooltip)
    {
        var button = new Button
        {
            Classes = { "bt", "ghost", "small" },
            Content = new IconPacks.Avalonia.Lucide.PackIconLucide { Kind = icon, Width = 14, Height = 14 },
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tooltip);
        Avalonia.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    public static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
