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

    /// <summary>
    /// The plugins that are turned on (and not hidden because toy features are off).
    /// Everything else in the app only looks at these.
    /// </summary>
    public static IReadOnlyList<IPlugin> Plugins => _plugins.Where(IsActive).ToList();

    /// <summary>Whether toy features (Intiface, vibrations, toy-only plugins) are on. See AppSettings.ToysEnabled.</summary>
    public static bool ToysEnabled => SettingsService.Current.ToysEnabled;

    /// <summary>False for toy-only plugins while toy features are off.</summary>
    public static bool IsAvailable(IPlugin plugin) => ToysEnabled || !plugin.UsesToys;

    private static bool IsActive(IPlugin plugin) => IsEnabled(plugin) && IsAvailable(plugin);

    /// <summary>
    /// Turns every toy feature on or off at once: disconnects Intiface and stops toy-only plugins when
    /// turning off, starts them again when turning back on. Pages update through PluginsChanged.
    /// </summary>
    public static async Task SetToysEnabled(bool enabled)
    {
        if (ToysEnabled == enabled) return;

        var toyPlugins = _plugins.Where(p => p.UsesToys && IsEnabled(p)).ToList();

        if (!enabled)
        {
            foreach (var plugin in toyPlugins.Where(_initialized.Contains)) await Safe(plugin, plugin.Stop);
            await ButthurtService.Stop();
        }

        SettingsService.Current.ToysEnabled = enabled;
        SettingsService.Save();

        if (enabled)
        {
            foreach (var plugin in toyPlugins)
            {
                if (!_initialized.Contains(plugin)) Initialize(plugin);
                else await Safe(plugin, plugin.Start);
            }
        }

        ActivityLog.Add(enabled ? "Toy features turned on" : "Toy features turned off, Butthurt is in streaming mode");
        PluginsChanged?.Invoke(null, EventArgs.Empty);
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

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

    public static bool IsEnabled(IPlugin plugin) => !SettingsService.Current.DisabledPlugins.Contains(plugin.Id);

    /// <summary>Initializes every plugin that is turned on. Called once at startup.</summary>
    public static void InitializeAll()
    {
        foreach (var plugin in Plugins) Initialize(plugin);
    }

    /// <summary>Turns a plugin on or off right away: it starts or stops and its pages appear or disappear.</summary>
    public static async Task SetEnabled(IPlugin plugin, bool enabled)
    {
        if (IsEnabled(plugin) == enabled) return;

        var disabled = SettingsService.Current.DisabledPlugins;
        disabled.Remove(plugin.Id);
        if (!enabled) disabled.Add(plugin.Id);
        SettingsService.Save();

        if (enabled && !IsAvailable(plugin))
        {
            // Remembered as on, it starts once toy features are turned back on.
        }
        else if (enabled)
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
