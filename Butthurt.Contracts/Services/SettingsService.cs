using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Butthurt.Contracts.Models;

namespace Butthurt.Contracts.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Butthurt");

    public static string SettingsFile => Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Current { get; private set; } = CreateDefaults();

    public static event EventHandler? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? CreateDefaults();
            }
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Could not read settings, using defaults ({e.Message})", LogLevel.Warning);
            Current = CreateDefaults();
        }

        FillMissingEvents(Current);
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(Current, JsonOptions));
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
        Current = CreateDefaults();
        Save();
    }

    public static EventSettings GetEvent(WowEventType type)
    {
        FillMissingEvents(Current);
        return Current.WowEvents[type];
    }

    private static AppSettings CreateDefaults()
    {
        var settings = new AppSettings();
        FillMissingEvents(settings);
        return settings;
    }

    private static void FillMissingEvents(AppSettings settings)
    {
        foreach (var definition in WowEvents.All)
        {
            if (!settings.WowEvents.ContainsKey(definition.Type))
            {
                settings.WowEvents[definition.Type] = new EventSettings
                {
                    Enabled = definition.Defaults.Enabled,
                    Intensity = definition.Defaults.Intensity,
                    DurationMs = definition.Defaults.DurationMs
                };
            }
        }
    }
}
