using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Butthurt.Contracts.Models;

namespace Butthurt.Contracts.Services;

public static class SettingsService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Live plugin settings objects. They are written back into AppSettings.Plugins on save.
    private static readonly Dictionary<string, object> _pluginSettings = new();
    private static readonly object _lock = new();

    /// <summary>%AppData%\Butthurt, or BUTTHURT_SETTINGS_DIR when set (portable installs, tests).</summary>
    public static string SettingsFolder { get; } =
        Environment.GetEnvironmentVariable("BUTTHURT_SETTINGS_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Butthurt");

    public static string SettingsFile => Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static event EventHandler? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Could not read settings, using defaults ({e.Message})", LogLevel.Warning);
            Current = new AppSettings();
        }

        lock (_lock) _pluginSettings.Clear();
    }

    /// <summary>
    /// The settings section of a plugin. Always returns the same object until settings are reset,
    /// so plugins can change it directly and call <see cref="Save"/>. Don't hold on to it across a reset.
    /// </summary>
    public static T GetPluginSettings<T>(string pluginId) where T : class, new()
    {
        lock (_lock)
        {
            if (_pluginSettings.TryGetValue(pluginId, out var cached)) return (T)cached;

            T? settings = null;
            try
            {
                if (Current.Plugins.TryGetValue(pluginId, out var section))
                {
                    settings = section.Deserialize<T>(JsonOptions);
                }
                else if (Current.Legacy is { Count: > 0 })
                {
                    // Settings saved before this plugin had its own section used the same property names.
                    settings = JsonSerializer.SerializeToElement(Current.Legacy, JsonOptions).Deserialize<T>(JsonOptions);
                }
            }
            catch (Exception e)
            {
                ActivityLog.Add($"Could not read the {pluginId} settings, using defaults ({e.Message})", LogLevel.Warning);
            }

            settings ??= new T();
            _pluginSettings[pluginId] = settings;
            return settings;
        }
    }

    public static void Save()
    {
        try
        {
            lock (_lock)
            {
                foreach (var (id, settings) in _pluginSettings)
                {
                    Current.Plugins[id] = JsonSerializer.SerializeToElement(settings, settings.GetType(), JsonOptions);
                }

                // Every plugin has read its old values by the time anything is saved.
                Current.Legacy = null;

                Directory.CreateDirectory(SettingsFolder);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(Current, JsonOptions));
            }
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Could not save settings ({e.Message})", LogLevel.Error);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static Timer? _saveTimer;

    /// <summary>Saves after things stop changing for a moment, for sliders that fire on every pixel dragged.</summary>
    public static void SaveSoon()
    {
        _saveTimer ??= new Timer(_ => Save());
        _saveTimer.Change(400, Timeout.Infinite);
    }

    public static void Reset()
    {
        lock (_lock)
        {
            Current = new AppSettings();
            _pluginSettings.Clear();
        }

        Save();
    }
}
