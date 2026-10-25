using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Butthurt.Contracts.Services;

namespace Butthurt.Contracts.Views;

/// <summary>Small helpers for building UI from code with the theme in Styles/Theme.axaml.</summary>
internal static class Ui
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

    /// <summary>Runs <see cref="AddonInstaller.AutoInstall"/> and tells the user how it went.</summary>
    public static async Task AutoInstallAddon()
    {
        try
        {
            var installed = AddonInstaller.AutoInstall();
            await NotificationService.ShowAsync(installed.Count == 0
                ? "Found World of Warcraft but no game versions to install into."
                : $"Addon installed for {string.Join(", ", installed.Select(f => f.DisplayName))}. Type /reload in game.", 5000);
        }
        catch (UnauthorizedAccessException)
        {
            await NotificationService.ShowAsync("Windows blocked the install. Try running Butthurt as administrator.", 5000);
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Addon install failed: {e.Message}", LogLevel.Error);
            await NotificationService.ShowAsync(e.Message, 5000);
        }
    }

    public static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
