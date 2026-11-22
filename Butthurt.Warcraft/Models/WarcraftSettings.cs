using System;
using System.Collections.Generic;
using System.Linq;

namespace Butthurt.Warcraft.Models;

public class EventSettings
{
    public bool Enabled { get; set; } = true;

    // 0.0 - 1.0
    public double Intensity { get; set; } = 0.5;
    public int DurationMs { get; set; } = 500;
}

/// <summary>
/// "When this happens in WoW". Shared by WoW's own toy rules and the PiShock rules on the WoW page.
/// </summary>
public class WowCondition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WowEventType Event { get; set; } = WowEventType.Death;
    public int CooldownSeconds { get; set; }
}

/// <summary>"When this happens in WoW, buzz the toys like this." As many as you like, also several per event.</summary>
public class WowRule : WowCondition
{
    public string Name { get; set; } = "";
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

    /// <summary>The toy rules. Null until first used, then filled from <see cref="WowEvents"/> (the old per-event settings).</summary>
    public List<WowRule>? Rules { get; set; }

    /// <summary>Before rules: one setting per event. Only read once, to make the first rules.</summary>
    public Dictionary<WowEventType, EventSettings> WowEvents { get; set; } = new();

    /// <summary>The rules, made from the old per-event settings (or the defaults) the first time.</summary>
    public List<WowRule> GetRules()
    {
        if (Rules != null)
        {
            // Rules for an event that was retired (Low health) can't fire anymore, drop them.
            Rules.RemoveAll(r => !Models.WowEvents.Exists(r.Event));
            return Rules;
        }

        Rules = Models.WowEvents.Original.Select(type =>
        {
            var definition = Models.WowEvents.Get(type);
            var old = WowEvents.TryGetValue(type, out var saved) ? saved : definition.Defaults;
            return new WowRule
            {
                Name = definition.Name,
                Event = type,
                Enabled = old.Enabled,
                Intensity = old.Intensity,
                DurationMs = old.DurationMs
            };
        }).ToList();

        return Rules;
    }
}
