using System;
using Butthurt.Contracts.Models;

namespace Butthurt.Contracts;

public interface IPageView
{
    string Title { get; }
    
    SideBarType SidebarType { get; }
    event EventHandler? BackRequested;
}
