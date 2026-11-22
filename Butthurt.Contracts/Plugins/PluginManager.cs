using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Butthurt.Contracts.Services;

namespace Butthurt.Contracts.Plugins;

/// <summary>
/// Keeps track of every plugin, built in or dropped into the Plugins folder, and which ones are turned on.
/// A turned off plugin is never initialized, so it doesn't run anything in the background.
/// </summary>
public static class PluginManager
{
    private static readonly List<IPlugin> _plugins = new();
    private static readonly HashSet<IPlugin> _initialized = new();

    /// <summary>Every known plugin, turned on or not (for the Settings page).</summary>
    public static IReadOnlyList<IPlugin> AllPlugins => _plugins;

    /// <summary>The plugins that are turned on. Everything else in the app only looks at these.</summary>
    public static IReadOnlyList<IPlugin> Plugins => _plugins.Where(IsEnabled).ToList();

    /// <summary>The Toys plugin's id (Butthurt.Toys), which Contracts can't reference directly.</summary>
    public const string ToysPluginId = "Toys";

    /// <summary>
    /// Whether toys (Intiface) are in use: the Toys plugin is loaded and turned on. When it's off, vibration
    /// rules sit out and the app talks about streaming instead of toys.
    /// </summary>
    public static bool ToysEnabled => IsOn(ToysPluginId);

    /// <summary>Raised when any plugin's status changes. May fire on any thread.</summary>
    public static event EventHandler? StatusChanged;

    /// <summary>Raised on the UI thread when a plugin is turned on or off.</summary>
    public static event EventHandler? PluginsChanged;

    public static void Register(IPlugin plugin)
    {
        if (_plugins.Any(p => p.Id == plugin.Id)) return;

        _plugins.Add(plugin);
        plugin.StatusChanged += (_, _) =>
        {
            if (IsEnabled(plugin)) StatusChanged?.Invoke(plugin, EventArgs.Empty);
        };
    }

    /// <summary>Loads extra plugins from DLLs in the given folder (next to the exe by default).</summary>
    public static void LoadFromFolder(string folder)
    {
        Directory.CreateDirectory(folder);

        foreach (var dllPath in Directory.GetFiles(folder, "*.dll"))
        {
            try
            {
                var types = Assembly.LoadFrom(dllPath).GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in types)
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin) Register(plugin);
                }
            }
            catch (Exception e)
            {
                ActivityLog.Add($"Couldn't load plugin {Path.GetFileName(dllPath)}: {e.Message}", LogLevel.Error);
            }
        }
    }

    /// <summary>Whether the plugin with this id is loaded and turned on.</summary>
    public static bool IsOn(string pluginId) => Plugins.Any(p => p.Id == pluginId);

    public static bool IsEnabled(IPlugin plugin) => !SettingsService.Current.DisabledPlugins.Contains(plugin.Id);

    /// <summary>Initializes every plugin that is turned on. Called once at startup.</summary>
    public static void InitializeAll()
    {
        MoveOldToySwitch();
        foreach (var plugin in Plugins) Initialize(plugin);
    }

    /// <summary>
    /// Before Toys was a plugin, Settings had one "Toy features" switch. If it was off, turn the Toys plugin off
    /// instead, once.
    /// </summary>
    private static void MoveOldToySwitch()
    {
        var settings = SettingsService.Current;
        if (settings.ToysEnabled) return;

        if (!settings.DisabledPlugins.Contains(ToysPluginId)) settings.DisabledPlugins.Add(ToysPluginId);
        settings.ToysEnabled = true;
        SettingsService.Save();
    }

    /// <summary>Turns a plugin on or off right away: it starts or stops and its pages appear or disappear.</summary>
    public static async Task SetEnabled(IPlugin plugin, bool enabled)
    {
        if (IsEnabled(plugin) == enabled) return;

        var disabled = SettingsService.Current.DisabledPlugins;
        disabled.Remove(plugin.Id);
        if (!enabled) disabled.Add(plugin.Id);
        SettingsService.Save();

        if (enabled)
        {
            // First time: Initialize does its own auto start. After that, start it like the Start button would.
            if (!_initialized.Contains(plugin)) Initialize(plugin);
            else await Safe(plugin, plugin.Start);
            ActivityLog.Add($"Turned on {plugin.Name}");
        }
        else
        {
            await Safe(plugin, plugin.Stop);
            ActivityLog.Add($"Turned off {plugin.Name}");
        }

        PluginsChanged?.Invoke(plugin, EventArgs.Empty);
        StatusChanged?.Invoke(plugin, EventArgs.Empty);
    }

    /// <summary>After settings were replaced (e.g. reset): start plugins that are now on and refresh the UI.</summary>
    public static void ApplySettings()
    {
        foreach (var plugin in Plugins) Initialize(plugin);
        PluginsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static Task StartAll() => Task.WhenAll(Plugins.Select(p => Safe(p, p.Start)));

    public static Task StopAll() => Task.WhenAll(Plugins.Select(p => Safe(p, p.Stop)));

    private static void Initialize(IPlugin plugin)
    {
        if (!_initialized.Add(plugin)) return;

        try
        {
            plugin.Initialize();
        }
        catch (Exception e)
        {
            ActivityLog.Add($"{plugin.Name} failed to start: {e.Message}", LogLevel.Error);
        }
    }

    private static async Task Safe(IPlugin plugin, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            ActivityLog.Add($"{plugin.Name}: {e.Message}", LogLevel.Error);
        }
    }
}
