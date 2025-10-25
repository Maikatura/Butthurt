using System;
using Avalonia.Controls;
using Buttplug.Client;

namespace Butthurt.Contracts.Views;
public partial class HomeView : UserControl, IPageView
{
    
    public string Title => "Home";
    public event EventHandler? BackRequested;  
    
    public HomeView()
    {
        InitializeComponent();
    }

     


    
    
}