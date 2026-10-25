using System;
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

    private bool _loading;
    private bool _confirmReset;

    public SettingsView()
    {
        InitializeComponent();

        AutoConnectToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            SettingsService.Current.AutoConnect = AutoConnectToggle.IsChecked == true;
            SettingsService.Save();
        };

        ToysToggle.IsCheckedChanged += async (_, _) =>
        {
            if (_loading) return;
            var on = ToysToggle.IsChecked == true;
            ToysToggle.IsEnabled = false;
            await PluginManager.SetToysEnabled(on);
            ToysToggle.IsEnabled = true;
            Load();
            await NotificationService.ShowAsync(on
                ? "Toy features are back."
                : "Toy features are off. Butthurt is now just for streaming and VRChat.");
        };

        AttachedToVisualTree += (_, _) => Load();
    }

    private void Load()
    {
        _loading = true;
        ToysToggle.IsChecked = PluginManager.ToysEnabled;
        StartupCard.IsVisible = PluginManager.ToysEnabled; // only holds the Intiface option
        AutoConnectToggle.IsChecked = SettingsService.Current.AutoConnect;
        SettingsPathText.Text = SettingsService.SettingsFile;
        _loading = false;

        _confirmReset = false;
        ResetButton.Content = "Reset";

        PluginList.Children.Clear();
        foreach (var plugin in PluginManager.AllPlugins)
        {
            PluginList.Children.Add(CreatePluginRow(plugin));
        }
    }

    private Control CreatePluginRow(IPlugin plugin)
    {
        var enabled = PluginManager.IsEnabled(plugin);
        var available = PluginManager.IsAvailable(plugin);
        var detail = Ui.Text(PluginDetail(plugin), "muted", 12);

        var toggle = new ToggleSwitch
        {
            IsChecked = enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = available
        };

        var row = new Border
        {
            Classes = { "row" },
            Opacity = enabled && available ? 1 : 0.6,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new PackIconLucide
                    {
                        Kind = plugin.Icon, Width = 18, Height = 18,
                        Foreground = new SolidColorBrush(Color.Parse("#D8B4FE")),
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
        return !PluginManager.IsAvailable(plugin) ? $"{assembly} · Needs toy features, turn them on above"
            : PluginManager.IsEnabled(plugin) ? $"{assembly} · {plugin.GetStatus().Headline}"
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
