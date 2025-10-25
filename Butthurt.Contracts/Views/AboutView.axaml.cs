using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Butthurt.Contracts.Models;

namespace Butthurt.Contracts.Views
{
public partial class AboutView : UserControl, IPageView
{
    public AboutView()
    {
        InitializeComponent();
    }

    public string Title => "About";
    
    public SideBarType SidebarType => SideBarType.Footer;
    
    public event EventHandler? BackRequested;   
    
    private void TwitterButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://twitter.com/maikatura") { UseShellExecute = true });
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/maikatura") { UseShellExecute = true });
    }

  


}
}