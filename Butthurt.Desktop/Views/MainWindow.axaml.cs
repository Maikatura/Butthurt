using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Twitch;
using Butthurt.VRChat;
using Butthurt.Warcraft;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly List<IPageView> _corePages = new();

    // Created the first time a plugin is turned on, then kept so its pages remember their state.
    private readonly Dictionary<IPlugin, List<IPageView>> _pluginPages = new();

    private readonly Dictionary<IPlugin, (Ellipse Dot, TextBlock Text)> _pluginStatusRows = new();
    private readonly Dictionary<IPageView, Button> _navButtons = new();
    private IPageView? _currentPage;

    public MainWindow()
    {
        InitializeComponent();

        // Built-in plugins 👇 (each one is its own project). Extra ones can be dropped into the Plugins folder.
        // They can be turned on and off on the Settings page.
        PluginManager.Register(new WarcraftPlugin());
        PluginManager.Register(new TwitchPlugin());
        PluginManager.Register(new VRChatPlugin());
        PluginManager.LoadFromFolder(System.IO.Path.Combine(AppContext.BaseDirectory, "Plugins"));

        _corePages.AddRange(new IPageView[]
        {
            new HomeView(),
            new ToysView(),
            new SettingsView(),
            new GuideView(),
            new RoadmapView(),
            new AboutView(),
        });

        StartServices();
        BuildSidebar();
        NavigateTo(_corePages[0]);

        NavigationService.NavigateRequested += (_, type) =>
        {
            var page = AllPages().FirstOrDefault(p => p.GetType() == type);
            if (page != null) NavigateTo(page);
        };
        NavigationService.NavigateToPluginRequested += (_, pluginId) =>
        {
            var plugin = PluginManager.Plugins.FirstOrDefault(p => p.Id == pluginId);
            if (plugin != null && PagesOf(plugin).FirstOrDefault() is { } page) NavigateTo(page);
        };

        PluginManager.PluginsChanged += (_, _) =>
        {
            BuildSidebar();

            // Don't leave the user looking at a page of a plugin they just turned off.
            if (_currentPage != null && !AllPages().Contains(_currentPage)) NavigateTo(_corePages[0]);
            else if (_currentPage != null) NavigateTo(_currentPage);

            UpdateStatus();
        };

        UpdateStatus();
    }

    private List<IPageView> PagesOf(IPlugin plugin)
    {
        if (!_pluginPages.TryGetValue(plugin, out var pages))
        {
            pages = plugin.CreatePages().ToList();
            _pluginPages[plugin] = pages;
        }

        return pages;
    }

    /// <summary>Core pages plus the pages of every plugin that is turned on. The Toys page goes away with toy features.</summary>
    private IEnumerable<IPageView> AllPages() =>
        _corePages.Where(p => p is not ToysView || PluginManager.ToysEnabled)
            .Concat(PluginManager.Plugins.SelectMany(PagesOf));

    private void BuildSidebar()
    {
        SidebarSections.Children.Clear();
        _navButtons.Clear();

        var pages = AllPages().ToList();
        foreach (SideBarType type in Enum.GetValues(typeof(SideBarType)))
        {
            var pagesInSection = pages.Where(p => p.SidebarType == type).ToList();
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

        PluginStatusRows.Children.Clear();
        _pluginStatusRows.Clear();
        foreach (var plugin in PluginManager.Plugins)
        {
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { FontSize = 12, Foreground = (IBrush)this.FindResource("MutedBrush")! };
            PluginStatusRows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { dot, text } });
            _pluginStatusRows[plugin] = (dot, text);
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
        PluginManager.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateStatus);

        PluginManager.InitializeAll();
        OverlayService.ApplySettings();

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

        IntifaceRow.IsVisible = PluginManager.ToysEnabled;
        ToysRow.IsVisible = PluginManager.ToysEnabled;

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

        foreach (var (plugin, (dot, text)) in _pluginStatusRows)
        {
            var status = plugin.GetStatus();
            dot.Fill = status.Level switch
            {
                PluginStatusLevel.Active => success,
                PluginStatusLevel.Waiting => warning,
                _ => off
            };
            text.Text = status.SidebarText;
        }
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
}
