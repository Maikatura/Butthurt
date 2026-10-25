using System;
using System.Collections.Generic;
using System.Linq;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Warcraft.Models;

public enum WowEventType
{
    Damage,
    Heal,
    Death,
    LevelUp,
    Achievement
}

public record WowEventDefinition(
    WowEventType Type,
    string Name,
    string Description,
    byte R, byte G, byte B,
    PackIconLucideKind Icon,
    EventSettings Defaults)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The colors the addon flashes in the top-left corner of the game window.
/// Keep these in sync with Butthurt.Colors in Addon/Retail/Butthurt/Butthurt.lua.
/// </summary>
public static class WowEvents
{
    public static readonly IReadOnlyList<WowEventDefinition> All = new[]
    {
        new WowEventDefinition(WowEventType.Damage, "Taking damage", "Any hit you take from mobs, players or the environment.",
            255, 0, 0, PackIconLucideKind.Swords, new EventSettings { Intensity = 0.6, DurationMs = 400 }),
        new WowEventDefinition(WowEventType.Heal, "Getting healed", "When a healer (or you) tops you up.",
            0, 255, 0, PackIconLucideKind.HeartPulse, new EventSettings { Enabled = false, Intensity = 0.25, DurationMs = 300 }),
        new WowEventDefinition(WowEventType.Death, "Dying", "Better luck next pull.",
            255, 0, 255, PackIconLucideKind.Skull, new EventSettings { Intensity = 1.0, DurationMs = 2500 }),
        new WowEventDefinition(WowEventType.LevelUp, "Level up", "Ding! A little reward for grinding.",
            0, 0, 255, PackIconLucideKind.Sparkles, new EventSettings { Intensity = 0.8, DurationMs = 1500 }),
        new WowEventDefinition(WowEventType.Achievement, "Achievement earned", "Fires on every achievement toast.",
            255, 255, 0, PackIconLucideKind.Trophy, new EventSettings { Intensity = 0.7, DurationMs = 1000 }),
    };

    public static WowEventDefinition Get(WowEventType type) => All.First(e => e.Type == type);

    /// <summary>Returns the event whose color matches the sampled pixel, or null when idle / unknown.</summary>
    public static WowEventDefinition? Match(byte r, byte g, byte b)
    {
        static bool Channel(byte actual, byte expected) => Math.Abs(actual - expected) < 60;

        return All.FirstOrDefault(e => Channel(r, e.R) && Channel(g, e.G) && Channel(b, e.B));
    }
}
