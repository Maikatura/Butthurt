using System;
using System.Collections.Generic;
using System.Threading;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Fansly.Models;

public enum FanslyTrigger
{
    Tip,
    Subscription,
    ChatCommand
}

/// <summary>Who wrote a chat line. Fansly sends no moderator flag, so this is what it has.</summary>
public enum FanslyChatterLevel
{
    Everyone,
    Followers,
    Subscribers,
    Creator
}

/// <summary>Something that happened in the creator's Fansly livestream chat.</summary>
public record FanslyEvent(FanslyTrigger Type, string UserName)
{
    /// <summary>Tips: the amount in dollars.</summary>
    public double TipDollars { get; init; }

    /// <summary>Subscriptions: the months in a row (1 for a new sub).</summary>
    public int Months { get; init; } = 1;

    /// <summary>Subscriptions: the tier's name as the creator called it. Chat: the sender's tier, empty when not subscribed.</summary>
    public string TierName { get; init; } = "";

    public string Message { get; init; } = "";
    public FanslyChatterLevel ChatterLevel { get; init; }

    private int _succeeded;
    private int _failed;

    /// <summary>Plugins that react to the event report back here, the same way as for Twitch events.</summary>
    public void ReportResult(bool success)
    {
        if (success) Interlocked.Increment(ref _succeeded);
        else Interlocked.Increment(ref _failed);
    }

    public bool AnySucceeded => _succeeded > 0;
    public bool OnlyFailed => _failed > 0 && _succeeded == 0;
}

/// <summary>
/// "When this happens on Fansly". Shared by every plugin that reacts to Fansly: Fansly's own vibration rules
/// and e.g. the PiShock rules embed one of these.
/// </summary>
public class FanslyCondition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public FanslyTrigger Trigger { get; set; }

    // Tips: the amount in dollars has to be in this range. 0 as maximum means no limit.
    public double MinTip { get; set; } = 1;
    public double MaxTip { get; set; }

    /// <summary>Subscriptions: tier names that count (as the creator named them). Empty means every tier.</summary>
    public List<string> Tiers { get; set; } = new();

    public bool IncludeResubs { get; set; } = true;

    // Chat commands: what the message has to start with and who may use it.
    public string Match { get; set; } = "";
    public FanslyChatterLevel WhoCanUse { get; set; } = FanslyChatterLevel.Everyone;

    public int CooldownSeconds { get; set; }
}

/// <summary>"When this happens on Fansly, vibrate like this."</summary>
public class FanslyRule : FanslyCondition
{
    public double Intensity { get; set; } = 0.5;
    public int DurationMs { get; set; } = 1000;

    /// <summary>Bigger tips buzz longer, up to 5x the duration.</summary>
    public bool ScaleWithAmount { get; set; }
}

public class FanslySettings
{
    /// <summary>The creator whose livestream chat is read (normally your own account).</summary>
    public string Username { get; set; } = "";

    // Looked up from the username, kept so a rename on Fansly doesn't need a new lookup every start.
    public string? AccountId { get; set; }
    public string? ChatRoomId { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>Tier names of the creator, for the subscription rules' pickers.</summary>
    public List<string> KnownTiers { get; set; } = new();

    /// <summary>
    /// Optional Fansly session token, only needed when Fansly refuses to let anonymous viewers read the chat.
    /// Stored encrypted for the current Windows user, see <c>Secrets</c>.
    /// </summary>
    public string? Token { get; set; }

    public bool AutoConnect { get; set; } = true;

    public List<FanslyRule>? Rules { get; set; }
}

public static class FanslyTriggers
{
    public static string DisplayName(this FanslyTrigger trigger) => trigger switch
    {
        FanslyTrigger.Tip => "Tip",
        FanslyTrigger.Subscription => "Subscription",
        FanslyTrigger.ChatCommand => "Chat command",
        _ => trigger.ToString()
    };

    public static PackIconLucideKind Icon(this FanslyTrigger trigger) => trigger switch
    {
        FanslyTrigger.Tip => PackIconLucideKind.DollarSign,
        FanslyTrigger.Subscription => PackIconLucideKind.Star,
        FanslyTrigger.ChatCommand => PackIconLucideKind.MessageSquare,
        _ => PackIconLucideKind.Zap
    };

    public static bool HasAmount(this FanslyTrigger trigger) => trigger == FanslyTrigger.Tip;

    public static List<FanslyRule> DefaultRules() => new()
    {
        new FanslyRule { Name = "Tips", Trigger = FanslyTrigger.Tip, MinTip = 1, MaxTip = 9.99, Intensity = 0.4, DurationMs = 1500 },
        new FanslyRule { Name = "Big tips", Trigger = FanslyTrigger.Tip, MinTip = 10, Intensity = 0.8, DurationMs = 3000, ScaleWithAmount = true },
        new FanslyRule { Name = "Subscriptions", Trigger = FanslyTrigger.Subscription, Intensity = 0.6, DurationMs = 2000 },
    };
}
