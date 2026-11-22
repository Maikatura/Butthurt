using System;
using System.Collections.Generic;
using System.Linq;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Warcraft.Models;

// New events go at the end: settings store these by name, so the order can change but names can't.
public enum WowEventType
{
    Damage,
    Heal,
    Death,
    LevelUp,
    Achievement,
    EnterCombat,
    LeaveCombat,
    LowHealth, // Retired: WoW 12.0 hides health from addons in combat. Kept so old settings still load.
    CrowdControl,
    Revive,
    BossPull,
    BossKill,
    QuestDone,
    PvpKill,
    Whisper,
    ReadyCheck
}

public record WowEventDefinition(
    WowEventType Type,
    string Name,
    string Description,
    string Category,
    byte R, byte G, byte B,
    PackIconLucideKind Icon,
    EventSettings Defaults)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The colors the addon flashes in the top-left corner of the game window.
/// Keep these in sync with Butthurt.Colors in Addon/Retail/Butthurt/Butthurt.lua.
/// Channels are 0, 128 or 255 and every two colors differ by at least 128 somewhere, so matching with a
/// tolerance of 60 can never mix them up.
/// </summary>
public static class WowEvents
{
    public const string Combat = "Combat";
    public const string Progress = "Progress";
    public const string Group = "Group & PvP";
    public const string Social = "Social";

    /// <summary>Category order for menus and lists.</summary>
    public static readonly IReadOnlyList<string> Categories = new[] { Combat, Progress, Group, Social };

    public static readonly IReadOnlyList<WowEventDefinition> All = new[]
    {
        // Combat
        new WowEventDefinition(WowEventType.Damage, "Taking damage", "Any hit you take from mobs, players or the environment.", Combat,
            255, 0, 0, PackIconLucideKind.Swords, new EventSettings { Intensity = 0.6, DurationMs = 400 }),
        new WowEventDefinition(WowEventType.Heal, "Getting healed", "When a healer (or you) tops you up.", Combat,
            0, 255, 0, PackIconLucideKind.HeartPulse, new EventSettings { Enabled = false, Intensity = 0.25, DurationMs = 300 }),
        new WowEventDefinition(WowEventType.CrowdControl, "Stunned or crowd controlled", "Stuns, fears, silences and other loss of control.", Combat,
            128, 0, 255, PackIconLucideKind.Lock, new EventSettings { Intensity = 0.7, DurationMs = 1000 }),
        new WowEventDefinition(WowEventType.EnterCombat, "Entering combat", "The moment you're pulled into a fight.", Combat,
            255, 128, 0, PackIconLucideKind.Flame, new EventSettings { Intensity = 0.4, DurationMs = 500 }),
        new WowEventDefinition(WowEventType.LeaveCombat, "Leaving combat", "When the fight is over and you can breathe.", Combat,
            0, 128, 255, PackIconLucideKind.ShieldCheck, new EventSettings { Intensity = 0.3, DurationMs = 400 }),
        new WowEventDefinition(WowEventType.Death, "Dying", "Better luck next pull.", Combat,
            255, 0, 255, PackIconLucideKind.Skull, new EventSettings { Intensity = 1.0, DurationMs = 2500 }),
        new WowEventDefinition(WowEventType.Revive, "Coming back to life", "Resurrected, or back from the spirit healer.", Combat,
            255, 255, 255, PackIconLucideKind.Sunrise, new EventSettings { Intensity = 0.5, DurationMs = 800 }),

        // Progress
        new WowEventDefinition(WowEventType.LevelUp, "Level up", "Ding! A little reward for grinding.", Progress,
            0, 0, 255, PackIconLucideKind.Sparkles, new EventSettings { Intensity = 0.8, DurationMs = 1500 }),
        new WowEventDefinition(WowEventType.Achievement, "Achievement earned", "Fires on every achievement toast.", Progress,
            255, 255, 0, PackIconLucideKind.Trophy, new EventSettings { Intensity = 0.7, DurationMs = 1000 }),
        new WowEventDefinition(WowEventType.QuestDone, "Quest turned in", "Every quest you hand in.", Progress,
            128, 255, 0, PackIconLucideKind.ScrollText, new EventSettings { Intensity = 0.4, DurationMs = 600 }),

        // Group content and PvP
        new WowEventDefinition(WowEventType.BossPull, "Boss pulled", "A raid or dungeon boss encounter starts.", Group,
            255, 0, 128, PackIconLucideKind.Crown, new EventSettings { Intensity = 0.6, DurationMs = 800 }),
        new WowEventDefinition(WowEventType.BossKill, "Boss killed", "The boss is down and the encounter is won.", Group,
            0, 255, 255, PackIconLucideKind.PartyPopper, new EventSettings { Intensity = 0.9, DurationMs = 2000 }),
        new WowEventDefinition(WowEventType.PvpKill, "Honorable kill", "You helped kill an enemy player.", Group,
            128, 128, 128, PackIconLucideKind.Crosshair, new EventSettings { Intensity = 0.7, DurationMs = 800 }),
        new WowEventDefinition(WowEventType.ReadyCheck, "Ready check", "Someone starts a ready check, so you don't miss it.", Group,
            255, 128, 255, PackIconLucideKind.CircleCheck, new EventSettings { Intensity = 0.5, DurationMs = 600 }),

        // Social
        new WowEventDefinition(WowEventType.Whisper, "Whisper received", "Someone whispers you (also Battle.net whispers).", Social,
            0, 255, 128, PackIconLucideKind.MessageCircle, new EventSettings { Intensity = 0.3, DurationMs = 400 }),
    };

    /// <summary>The events the app had before rules, which get a rule each when old settings are moved over.</summary>
    public static readonly IReadOnlyList<WowEventType> Original = new[]
    {
        WowEventType.Damage, WowEventType.Heal, WowEventType.Death, WowEventType.LevelUp, WowEventType.Achievement
    };

    public static WowEventDefinition Get(WowEventType type) => All.First(e => e.Type == type);

    /// <summary>False for retired events (still in the enum so old settings load), which rules can't use anymore.</summary>
    public static bool Exists(WowEventType type) => All.Any(e => e.Type == type);

    public static IEnumerable<WowEventDefinition> InCategory(string category) => All.Where(e => e.Category == category);

    /// <summary>
    /// The box the addon draws right after its event boxes (Butthurt.MarkerColor in Butthurt.lua). Where it
    /// starts and how wide it is tells the watcher how many boxes there are and how big they are. No event
    /// uses this color.
    /// </summary>
    public const byte MarkerR = 128, MarkerG = 0, MarkerB = 128;

    private static bool Channel(byte actual, byte expected) => Math.Abs(actual - expected) < 60;

    /// <summary>Returns the event whose color matches the sampled pixel, or null when idle / unknown.</summary>
    public static WowEventDefinition? Match(byte r, byte g, byte b) =>
        All.FirstOrDefault(e => Channel(r, e.R) && Channel(g, e.G) && Channel(b, e.B));

    public static bool IsMarker(byte r, byte g, byte b) => Channel(r, MarkerR) && Channel(g, MarkerG) && Channel(b, MarkerB);

    public static bool IsIdle(byte r, byte g, byte b) => Channel(r, 0) && Channel(g, 0) && Channel(b, 0);
}
