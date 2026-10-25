using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Butthurt.Contracts.Models;
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

        AutoWatchToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            SettingsService.Current.AutoStartWatcher = AutoWatchToggle.IsChecked == true;
            SettingsService.Save();
        };

        PollSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var value = (int)(Math.Round(PollSlider.Value / 5) * 5);
            PollText.Text = $"{value} ms";
            if (_loading) return;
            SettingsService.Current.PollIntervalMs = value;
            SettingsService.SaveSoon();
        };

        AttachedToVisualTree += (_, _) => Load();
    }

    private void Load()
    {
        _loading = true;
        var settings = SettingsService.Current;
        AutoConnectToggle.IsChecked = settings.AutoConnect;
        AutoWatchToggle.IsChecked = settings.AutoStartWatcher;
        PollSlider.Value = settings.PollIntervalMs;
        PollText.Text = $"{settings.PollIntervalMs} ms";
        SettingsPathText.Text = SettingsService.SettingsFile;
        _loading = false;

        _confirmReset = false;
        ResetButton.Content = "Reset";
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
        Load();
        await NotificationService.ShowAsync("Settings reset to defaults.");
    }
}
