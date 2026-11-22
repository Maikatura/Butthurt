using System;
using System.Collections.Generic;
using Butthurt.Fansly.Models;
using Butthurt.Twitch.Models;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.PiShock.Models;

/// <summary>What the shocker does. The numbers are PiShock's own "Op" codes.</summary>
public enum PiShockOp
{
    Shock = 0,
    Vibrate = 1,
    Beep = 2
}

/// <summary>Where the events of a rule come from.</summary>
public enum PiShockSource
{
    Twitch,
    Fansly
}

public enum ShockerTarget
{
    /// <summary>Every shocker that is turned on.</summary>
    All,

    /// <summary>One shocker picked at random.</summary>
    Random,

    /// <summary>The shocker in <see cref="PiShockRule.ShockerId"/>.</summary>
    One
}

/// <summary>One PiShock shocker, reached through a share code made on pishock.com.</summary>
public class PiShockShocker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ShareCode { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Nothing goes above this, whatever a rule says (1–100). The share code's own limits on pishock.com apply too.</summary>
    public int MaxIntensity { get; set; } = 50;

    /// <summary>Longest a single command may last in seconds (1–15).</summary>
    public int MaxDurationSeconds { get; set; } = 5;
}

/// <summary>"When this happens on Twitch or Fansly, shock / vibrate / beep like this."</summary>
public class PiShockRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public PiShockSource Source { get; set; } = PiShockSource.Twitch;

    /// <summary>Used when <see cref="Source"/> is Twitch: the same conditions Twitch's own rules use.</summary>
    public TwitchCondition Twitch { get; set; } = new() { Trigger = TwitchTrigger.Bits, MinAmount = 100 };

    /// <summary>Used when <see cref="Source"/> is Fansly.</summary>
    public FanslyCondition Fansly { get; set; } = new() { Trigger = FanslyTrigger.Tip, MinTip = 5 };

    public PiShockOp Op { get; set; } = PiShockOp.Vibrate;

    /// <summary>1–100, capped by each shocker's maximum. Beeps have no intensity.</summary>
    public int Intensity { get; set; } = 25;

    /// <summary>1–15 seconds, capped by each shocker's maximum.</summary>
    public int DurationSeconds { get; set; } = 1;

    public ShockerTarget Target { get; set; } = ShockerTarget.All;
    public string ShockerId { get; set; } = "";

    /// <summary>Bigger amounts (bits, gifts, raid viewers, tips) last longer, up to 5x, still within the shocker's maximum.</summary>
    public bool ScaleWithAmount { get; set; }

    /// <summary>Beep first and wait a moment before a shock, so it never comes out of nowhere.</summary>
    public bool WarnFirst { get; set; }
}

public class PiShockSettings
{
    /// <summary>The pishock.com username the API key belongs to.</summary>
    public string Username { get; set; } = "";

    /// <summary>Stored encrypted for the current Windows user, see <c>Secrets</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Shown in the PiShock logs as who sent the command (the viewer's name is added after it).</summary>
    public string SenderName { get; set; } = "Butthurt";

    /// <summary>Master switch for shocks. Off: shock rules and shock tests do nothing, vibrations and beeps still work.</summary>
    public bool ShocksAllowed { get; set; } = true;

    /// <summary>Nothing is sent while paused and waiting commands are dropped.</summary>
    public bool Paused { get; set; }

    public List<PiShockShocker> Shockers { get; set; } = new();

    public List<PiShockRule>? Rules { get; set; }

    public static List<PiShockRule> DefaultRules() => new()
    {
        new PiShockRule
        {
            Name = "Buzz for bits", Op = PiShockOp.Vibrate, Intensity = 40, DurationSeconds = 2, ScaleWithAmount = true,
            Source = PiShockSource.Twitch, Twitch = new TwitchCondition { Trigger = TwitchTrigger.Bits, MinAmount = 100 }
        },
        new PiShockRule
        {
            Name = "Zap for big bits", Enabled = false, Op = PiShockOp.Shock, Intensity = 15, DurationSeconds = 1, WarnFirst = true,
            Source = PiShockSource.Twitch, Twitch = new TwitchCondition { Trigger = TwitchTrigger.Bits, MinAmount = 1000, CooldownSeconds = 30 }
        },
        new PiShockRule
        {
            Name = "Buzz for Fansly tips", Op = PiShockOp.Vibrate, Intensity = 50, DurationSeconds = 2, ScaleWithAmount = true,
            Source = PiShockSource.Fansly, Fansly = new FanslyCondition { Trigger = FanslyTrigger.Tip, MinTip = 5 }
        },
    };
}

public static class PiShockOps
{
    public static string DisplayName(this PiShockOp op) => op switch
    {
        PiShockOp.Shock => "Shock",
        PiShockOp.Vibrate => "Vibrate",
        PiShockOp.Beep => "Beep",
        _ => op.ToString()
    };

    public static PackIconLucideKind Icon(this PiShockOp op) => op switch
    {
        PiShockOp.Shock => PackIconLucideKind.Zap,
        PiShockOp.Vibrate => PackIconLucideKind.Vibrate,
        PiShockOp.Beep => PackIconLucideKind.BellRing,
        _ => PackIconLucideKind.Zap
    };
}
