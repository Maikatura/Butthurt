using System;

namespace Butthurt.Contracts.Services;

/// <summary>Lets pages jump to other pages without knowing about the main window.</summary>
public static class NavigationService
{
    public static event EventHandler<Type>? NavigateRequested;

    /// <summary>Asks for the first page of a plugin, by plugin id.</summary>
    public static event EventHandler<string>? NavigateToPluginRequested;

    public static void NavigateTo<T>() where T : IPageView => NavigateRequested?.Invoke(null, typeof(T));

    public static void NavigateToPlugin(string pluginId) => NavigateToPluginRequested?.Invoke(null, pluginId);
}
