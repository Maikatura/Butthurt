using System;
using Avalonia.Controls;
using Avalonia.Media;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Services;
using Buttplug.Client;

namespace Butthurt.Contracts.Views;
public partial class HomeView : UserControl, IPageView
{
    
    public string Title => "Home";

    public SideBarType SidebarType => SideBarType.Header;
    public event EventHandler? BackRequested;  
    
    public HomeView()
    {
        InitializeComponent();
        
        this.AttachedToVisualTree += (_, __) => UpdateButton();
    }

    private async void StartStopButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!ButthurtService.IsConnected)
        {
            await ButthurtService.Start();

            foreach (var names in ButthurtService.GetDevices())
            {
                DeviceList.Children.Add(new TextBlock()
                {
                    Text = names
                });      
            }
            
          
        }
        else
        {
            await ButthurtService.Stop();
            DeviceList.Children.Clear();
        }

        UpdateButton();
    }

    private void UpdateButton()
    {
        if (StartStopButton == null)
            return;

        if (ButthurtService.IsConnected)
        {
            StartStopButton.Content = "⛔ Stop";
            StartStopButton.Background = Brushes.Crimson;
        }
        else
        {
            StartStopButton.Content = "▶ Start";
            StartStopButton.Background = Brushes.MediumSeaGreen;
        }
    }


    
    
}