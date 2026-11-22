using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Views;

public partial class SettingsView : UserControl, IPageView
{
    public string Title => "Settings";
    public PackIconLucideKind Icon => PackIconLucideKind.Settings;
    public SideBarType SidebarType => SideBarType.Footer;
    public event EventHandler? BackRequested;

    private bool _confirmReset;

    public SettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Load();
    }

    private void Load()
    {
        SettingsPathText.Text = SettingsService.SettingsFile;

        _confirmReset = false;
        ResetButton.Content = "Reset";

        // Grouped like the sidebar: Hardware, Streaming, Games.
        PluginList.Children.Clear();
        foreach (var group in PluginManager.AllPlugins.GroupBy(p => p.Category).OrderBy(g => g.Key))
        {
            var caption = Ui.Text(group.Key.DisplayName(), "caption");
            caption.Margin = new Thickness(2, PluginList.Children.Count == 0 ? 0 : 10, 0, 0);
            PluginList.Children.Add(caption);
            foreach (var plugin in group) PluginList.Children.Add(CreatePluginRow(plugin));
        }
    }

    private Control CreatePluginRow(IPlugin plugin)
    {
        var enabled = PluginManager.IsEnabled(plugin);
        var detail = Ui.Text(PluginDetail(plugin), "muted", 12);

        var toggle = new ToggleSwitch
        {
            IsChecked = enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center
        };

        var row = new Border
        {
            Classes = { "row" },
            Opacity = enabled ? 1 : 0.6,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new PackIconLucide
                    {
                        Kind = plugin.Icon, Width = 18, Height = 18,
                        Foreground = new SolidColorBrush(Color.Parse("#FAFAFA")),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 14, 0)
                    },
                    Ui.WithColumn(new StackPanel
                    {
                        Spacing = 2,
                        Children = { Ui.Text(plugin.Name, "h3"), detail }
                    }, 1),
                    Ui.WithColumn(toggle, 2)
                }
            }
        };

        toggle.IsCheckedChanged += async (_, _) =>
        {
            var on = toggle.IsChecked == true;
            toggle.IsEnabled = false;
            await PluginManager.SetEnabled(plugin, on);
            toggle.IsEnabled = true;

            row.Opacity = on ? 1 : 0.6;
            detail.Text = PluginDetail(plugin);
            await NotificationService.ShowAsync(on ? $"{plugin.Name} turned on" : $"{plugin.Name} turned off and unloaded from the sidebar");
        };

        return row;
    }

    private static string PluginDetail(IPlugin plugin)
    {
        var assembly = plugin.GetType().Assembly.GetName().Name;
        return PluginManager.IsEnabled(plugin) ? $"{assembly} · {plugin.GetStatus().Headline}"
            : $"{assembly} · Off, nothing from this plugin runs";
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsService.SettingsFolder);
        Process.Start(new ProcessStartInfo(SettingsService.SettingsFolder) { UseShellExecute = true });
    }

    private async void Reset_Click(object? sender, RoutedEventArgs e)
    {
        if (!_confirmReset)
        {
            _confirmReset = true;
            ResetButton.Content = "Click again to confirm";
            return;
        }

        SettingsService.Reset();
        PluginManager.ApplySettings();
        Load();
        await NotificationService.ShowAsync("Settings reset to defaults.");
    }
}
