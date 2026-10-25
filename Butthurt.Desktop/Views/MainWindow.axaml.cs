using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly List<IPlugin> _plugins = new();

    private readonly List<IPageView> _pages = new();
    private readonly Dictionary<IPageView, Button> _navButtons = new();
    private IPageView? _currentPage;

    public MainWindow()
    {
        InitializeComponent();
        StartServices();
        LoadPlugins();

        // Register your pages in order here — that's it 👇
        _pages.AddRange(new IPageView[]
        {
            new HomeView(),
            new ToysView(),
            new WowView(),
            new SettingsView(),
            new GuideView(),
            new RoadmapView(),
            new AboutView(),
        });

        BuildSidebar();

        var firstPage = _pages.FirstOrDefault(p => p.SidebarType == SideBarType.Header) ?? _pages.FirstOrDefault();
        if (firstPage != null) NavigateTo(firstPage);

        NavigationService.NavigateRequested += (_, type) =>
        {
            var page = _pages.FirstOrDefault(p => p.GetType() == type);
            if (page != null) NavigateTo(page);
        };

        UpdateStatus();
    }

    private void BuildSidebar()
    {
        foreach (SideBarType type in Enum.GetValues(typeof(SideBarType)))
        {
            var pagesInSection = _pages.Where(p => p.SidebarType == type).ToList();
            if (pagesInSection.Count == 0) continue;

            SidebarSections.Children.Add(new TextBlock
            {
                Text = type.DisplayName(),
                Classes = { "caption" },
                Margin = new Thickness(12, SidebarSections.Children.Count == 0 ? 0 : 18, 0, 6)
            });

            foreach (var page in pagesInSection)
            {
                var btn = new Button
                {
                    Classes = { "bt", "nav" },
                    Tag = page,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            new PackIconLucide { Kind = page.Icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = page.Title, VerticalAlignment = VerticalAlignment.Center }
                        }
                    }
                };

                btn.Click += (s, e) =>
                {
                    if (s is Button { Tag: IPageView selected }) NavigateTo(selected);
                };

                _navButtons[page] = btn;
                SidebarSections.Children.Add(btn);
            }
        }
    }

    private void StartServices()
    {
        NotificationService.Initialize(NotificationPanel);

        ButthurtService.DeviceAdded += (_, args) =>
        {
            _ = NotificationService.ShowAsync($"Toy connected: {args.Device.Name}");
        };
        ButthurtService.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        WowWatcherService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);
        WowWatcherService.EventDetected += (_, e) => Dispatcher.UIThread.Post(UpdateStatus);

        if (SettingsService.Current.AutoStartWatcher)
        {
            WowWatcherService.Start();
        }

        if (SettingsService.Current.AutoConnect)
        {
            _ = ButthurtService.Start();
        }
    }

    private void UpdateStatus()
    {
        var success = (IBrush)this.FindResource("SuccessBrush")!;
        var warning = (IBrush)this.FindResource("WarningBrush")!;
        var off = (IBrush)this.FindResource("DimBrush")!;

        if (ButthurtService.IsConnected)
        {
            IntifaceDot.Fill = success;
            IntifaceStatusText.Text = "Intiface connected";
        }
        else
        {
            IntifaceDot.Fill = ButthurtService.IsConnecting ? warning : off;
            IntifaceStatusText.Text = ButthurtService.IsConnecting ? "Connecting…" : "Intiface offline";
        }

        var toyCount = ButthurtService.Devices.Count;
        ToysDot.Fill = toyCount > 0 ? success : ButthurtService.IsScanning ? warning : off;
        ToysStatusText.Text = ButthurtService.IsScanning ? "Scanning for toys…"
            : toyCount == 1 ? "1 toy ready"
            : $"{toyCount} toys ready";

        WowDot.Fill = !WowWatcherService.IsRunning ? off : WowWatcherService.IsWowFocused ? success : warning;
        WowStatusText.Text = !WowWatcherService.IsRunning ? "Watcher stopped"
            : WowWatcherService.IsWowFocused ? "Watching WoW"
            : "Waiting for WoW";
    }

    private void NavigateTo(IPageView page)
    {
        if (_currentPage != null && _navButtons.TryGetValue(_currentPage, out var oldButton))
        {
            oldButton.Classes.Remove("active");
        }

        _currentPage = page;
        if (_navButtons.TryGetValue(page, out var newButton))
        {
            newButton.Classes.Add("active");
        }

        MainContent.Content = (Control)page;
    }

    private void LoadPlugins()
    {
        string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        Directory.CreateDirectory(pluginsDir);

        foreach (var dllPath in Directory.GetFiles(pluginsDir, "*.dll"))
        {
            try
            {
                var asm = Assembly.LoadFrom(dllPath);
                var pluginTypes = asm.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin)
                    {
                        plugin.Initialize();
                        _plugins.Add(plugin);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Plugin load failed: {ex.Message}");
            }
        }
    }
}
