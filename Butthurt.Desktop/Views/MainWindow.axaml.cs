using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
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
            new RoadmapView(),
            new AboutView(),
            //new HelpView()
        });

        // Dictionary to hold StackPanels for each section
        var sectionPanels = new Dictionary<SideBarType, StackPanel>();

// Loop through all enum values
        foreach (SideBarType type in Enum.GetValues(typeof(SideBarType)))
        {
            // Create header
            var header = new TextBlock
            {
                Text = type.ToString(),
                Foreground = Brushes.Gray,
                Margin = new Thickness(5)
            };

            // Create panel for buttons
            var panel = new StackPanel { Spacing = 0 };
            sectionPanels[type] = panel;

            // Add to main sidebar container
            SidebarSections.Children.Add(header);
            SidebarSections.Children.Add(panel);
        }

// Create buttons dynamically
        foreach (var page in _pages)
        {
            var btn = new Button
            {
                Content = page.Title,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Tag = page // store the page in Tag
            };

            btn.Click += (s, e) => 
            {
                if (s is Button { Tag: IPageView selected }) NavigateTo(selected);
            };

            // Add button to the correct section dynamically
            sectionPanels[page.SidebarType].Children.Add(btn);
        }

        try
        {
            NavigateTo((IPageView)sectionPanels[SideBarType.Header].Children.First().Tag);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Tried to go to the first header item but it was not found.");
        }
        
    }

    private async void StartServices()
    {
        try
        {
            NotificationService.Initialize(NotificationPanel);
            //await ButthurtService.Start(); // ✅ async, non-blocking
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return;
        }

        ButthurtService.DeviceAdded += (obj, addDevice) =>
        {
            // Ensure all UI work happens on the UI thread
            Dispatcher.UIThread.Post(async () =>
            {
                await NotificationService.ShowAsync("Device added");
            });
        };

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