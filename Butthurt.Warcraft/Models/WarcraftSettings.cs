using System.Collections.Generic;

namespace Butthurt.Warcraft.Models;

public class EventSettings
{
    public bool Enabled { get; set; } = true;

    // 0.0 - 1.0
    public double Intensity { get; set; } = 0.5;
    public int DurationMs { get; set; } = 500;
}

/// <summary>
/// Settings of the WoW plugin. The property names match the ones the app used before plugins had their
/// own settings section, so old settings carry over.
/// </summary>
public class WarcraftSettings
{
    public bool AutoStartWatcher { get; set; } = true;
    public int PollIntervalMs { get; set; } = 50;
    public string? WowPath { get; set; }
    public Dictionary<WowEventType, EventSettings> WowEvents { get; set; } = new();

    public EventSettings GetEvent(WowEventType type)
    {
        if (!WowEvents.TryGetValue(type, out var settings))
        {
            var defaults = Models.WowEvents.Get(type).Defaults;
            settings = WowEvents[type] = new EventSettings
            {
                Enabled = defaults.Enabled,
                Intensity = defaults.Intensity,
                DurationMs = defaults.DurationMs
            };
        }

        return settings;
    }
}
