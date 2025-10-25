using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.ViewModels;

namespace Butthurt.Contracts.Views
{
public partial class RoadmapView : UserControl, IPageView
{
    
    public string Title => "Roadmap";
    public event EventHandler? BackRequested;   


    public RoadmapView()
    {
        InitializeComponent();
    }

    
    
}
}