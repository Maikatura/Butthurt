using System;
using Butthurt.Contracts.Models;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts;

public interface IPageView
{
    string Title { get; }

    PackIconLucideKind Icon { get; }

    SideBarType SidebarType { get; }
    event EventHandler? BackRequested;
}
