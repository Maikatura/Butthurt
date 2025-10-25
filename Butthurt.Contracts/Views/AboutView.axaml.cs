using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Butthurt.Contracts.Models;

namespace Butthurt.Contracts.Views;

public partial class AboutView : UserControl, IPageView
{
    public string Title => "About";
    
    public SideBarType SidebarType => SideBarType.Footer;
    
    public event EventHandler? BackRequested;   
    
    public AboutView()
    {
        InitializeComponent();
        
    }

   
    
    private void WebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://maikatura.net") { UseShellExecute = true });
    }
    
    private void TwitterButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://twitter.com/maikatura") { UseShellExecute = true });
    }
    
    

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/maikatura") { UseShellExecute = true });
    }
    
    


}