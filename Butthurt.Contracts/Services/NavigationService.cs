using System;

namespace Butthurt.Contracts.Services;

/// <summary>Lets pages jump to other pages without knowing about the main window.</summary>
public static class NavigationService
{
    public static event EventHandler<Type>? NavigateRequested;

    public static void NavigateTo<T>() where T : IPageView => NavigateRequested?.Invoke(null, typeof(T));
}
