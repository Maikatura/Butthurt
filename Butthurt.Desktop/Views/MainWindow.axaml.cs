using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Butthurt.Contracts;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;

namespace Butthurt.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly List<IPlugin> _plugins = new();

    private readonly List<IPageView> _pages = new();
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
            new AboutView(),
            new RoadmapView(),
            //new HelpView()
        });

        // Create sidebar buttons automatically
        foreach (var page in _pages)
        {
            var btn = new Button
            {
                Content = page.Title,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Tag = page // store the page in Tag
            };

            btn.Click += (s, e) =>
            {
                var selected = (IPageView)((Button)s!).Tag!;
                NavigateTo(selected);
            };

            Sidebar.Children.Add(btn);
        }

        // Hook up back buttons
        foreach (var page in _pages)
        {
            page.BackRequested += (_, _) => NavigateTo(_pages[0]); // 0 = Home
        }

        // Start at Home
        NavigateTo(_pages[0]);
        
    }

    private async void StartServices()
    {
        try
        {
            await ButthurtService.Start(); // ✅ async, non-blocking
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return;
        }

        ButthurtService.DeviceAdded += (obj, addDevice) => {
            ShowNotification("New Device Added");
        };
    }

    
    
    
    private void ShowNotification(string message, int durationMs = 3000)
    {
        var notification = new NotificationControl(message);
        _ = notification.ShowAsync(NotificationPanel, durationMs); // fire-and-forget
    }

    
    private void NavigateTo(IPageView page)
    {
        _currentPage = page;
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
                        //PluginList.Items.Add(plugin.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Plugin load failed: {ex.Message}");
            }
        }
    }

    private void RunPluginButton_Click(object? sender, RoutedEventArgs e)
    {
        //if (PluginList.SelectedItem is string pluginName)
        //{
        //    var plugin = _plugins.FirstOrDefault(p => p.Name == pluginName);
        //    if (plugin != null)
        //    {
        //        string result = plugin.Execute(InputBox.Text ?? "");
        //        OutputBlock.Text = result;
        //    }
        //}
    }
}