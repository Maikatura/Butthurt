using System;
using System.Collections.Generic;
using System.Threading;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Twitch.Models;

public enum TwitchTrigger
{
    Bits,
    Subscription,
    GiftSub,
    Redeem,
    Follow,
    Raid,
    ChatCommand
}

public enum ChatterLevel
{
    Everyone,
    Subscribers,
    Vips,
    Moderators
}

/// <summary>Something that happened on the channel, already translated from Twitch's EventSub payloads.</summary>
public record TwitchEvent(TwitchTrigger Type, string UserName)
{
    /// <summary>Bits cheered, subs gifted, raid viewers or total months subscribed.</summary>
    public int Amount { get; init; }

    /// <summary>1, 2 or 3. Twitch reports Prime as tier 1.</summary>
    public int Tier { get; init; }

    public bool IsResub { get; init; }
    public string RewardTitle { get; init; } = "";
    public string Message { get; init; } = "";
    public ChatterLevel ChatterLevel { get; init; }

    // Redeems only: needed to mark Butthurt's own rewards as fulfilled or refund them.
    public string RewardId { get; init; } = "";
    public string RedemptionId { get; init; } = "";

    private int _succeeded;
    private int _failed;

    /// <summary>
    /// Plugins that react to the event report back here. For Butthurt's own channel point rewards the
    /// redemption is refunded when everything that tried to react failed (e.g. VRChat wasn't running).
    /// </summary>
    public void ReportResult(bool success)
    {
        if (success) Interlocked.Increment(ref _succeeded);
        else Interlocked.Increment(ref _failed);
    }

    public bool AnySucceeded => _succeeded > 0;
    public bool OnlyFailed => _failed > 0 && _succeeded == 0;
}

/// <summary>
/// "When this happens on Twitch". Shared by every plugin that reacts to Twitch: Twitch's own vibration rules
/// and e.g. the VRChat rules embed one of these.
/// </summary>
public class TwitchCondition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public TwitchTrigger Trigger { get; set; }

    // Bits / gift subs / raids: the amount has to be in this range. 0 as maximum means no limit.
    public int MinAmount { get; set; } = 1;
    public int MaxAmount { get; set; }

    // Subs and gift subs: which tiers count.
    public List<int> Tiers { get; set; } = new() { 1, 2, 3 };
    public bool IncludeResubs { get; set; } = true;

    // Redeems: the reward's title. Chat commands: what the message has to start with.
    public string Match { get; set; } = "";
    public ChatterLevel WhoCanUse { get; set; } = ChatterLevel.Everyone;

    public int CooldownSeconds { get; set; }

    // Redeems: Butthurt creates the reward (named Match) on Twitch, keeps its cost in sync, hides it while
    // the rule is off and deletes it with the rule.
    public bool ManageReward { get; set; }
    public int RewardCost { get; set; } = 500;
}

/// <summary>"When this happens on Twitch, vibrate like this." Users make as many as they want.</summary>
public class TwitchRule : TwitchCondition
{
    public double Intensity { get; set; } = 0.5;
    public int DurationMs { get; set; } = 1000;

    /// <summary>Bigger amounts buzz longer, up to 5x the duration.</summary>
    public bool ScaleWithAmount { get; set; }
}

/// <summary>A channel point reward Butthurt created, so it can also fulfil or refund its redemptions.</summary>
public class ManagedReward
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Cost { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Created for a rule (instead of by hand on the Twitch page). Kept in sync with the rules and deleted
    /// when no rule asks for it anymore.
    /// </summary>
    public bool OwnedByRules { get; set; }
}

/// <summary>A reward some rule wants to exist on Twitch, see TwitchService.RegisterRewardSource.</summary>
public record DesiredReward(string Title, int Cost, bool Enabled);

public class TwitchSettings
{
    /// <summary>Overrides <c>TwitchService.DefaultClientId</c> for people who registered their own Twitch app.</summary>
    public string ClientId { get; set; } = "";

    // Stored encrypted for the current Windows user, see TwitchService.
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }

    public string? UserId { get; set; }
    public string? Login { get; set; }
    public bool AutoConnect { get; set; } = true;

    public List<TwitchRule>? Rules { get; set; }
    public List<ManagedReward> ManagedRewards { get; set; } = new();
}

public static class TwitchTriggers
{
    public static string DisplayName(this TwitchTrigger trigger) => trigger switch
    {
        TwitchTrigger.Bits => "Bits",
        TwitchTrigger.Subscription => "Subscription",
        TwitchTrigger.GiftSub => "Gifted subs",
        TwitchTrigger.Redeem => "Channel points redeem",
        TwitchTrigger.Follow => "Follow",
        TwitchTrigger.Raid => "Raid",
        TwitchTrigger.ChatCommand => "Chat command",
        _ => trigger.ToString()
    };

    public static PackIconLucideKind Icon(this TwitchTrigger trigger) => trigger switch
    {
        TwitchTrigger.Bits => PackIconLucideKind.Gem,
        TwitchTrigger.Subscription => PackIconLucideKind.Star,
        TwitchTrigger.GiftSub => PackIconLucideKind.Gift,
        TwitchTrigger.Redeem => PackIconLucideKind.Coins,
        TwitchTrigger.Follow => PackIconLucideKind.Heart,
        TwitchTrigger.Raid => PackIconLucideKind.Users,
        TwitchTrigger.ChatCommand => PackIconLucideKind.MessageSquare,
        _ => PackIconLucideKind.Zap
    };

    /// <summary>Whether "amount" means something for this trigger (and scaling makes sense).</summary>
    public static bool HasAmount(this TwitchTrigger trigger) =>
        trigger is TwitchTrigger.Bits or TwitchTrigger.GiftSub or TwitchTrigger.Raid;

    public static List<TwitchRule> DefaultRules() => new()
    {
        new TwitchRule { Name = "Cheers", Trigger = TwitchTrigger.Bits, MinAmount = 1, MaxAmount = 499, Intensity = 0.4, DurationMs = 1000 },
        new TwitchRule { Name = "Big cheers", Trigger = TwitchTrigger.Bits, MinAmount = 500, Intensity = 0.8, DurationMs = 2500, ScaleWithAmount = true },
        new TwitchRule { Name = "Tier 1 subs", Trigger = TwitchTrigger.Subscription, Tiers = new() { 1 }, Intensity = 0.5, DurationMs = 1500 },
        new TwitchRule { Name = "Tier 2 & 3 subs", Trigger = TwitchTrigger.Subscription, Tiers = new() { 2, 3 }, Intensity = 1.0, DurationMs = 3000 },
        new TwitchRule { Name = "Gift bombs", Trigger = TwitchTrigger.GiftSub, MinAmount = 1, Intensity = 0.7, DurationMs = 1500, ScaleWithAmount = true },
        new TwitchRule { Name = "Raids", Trigger = TwitchTrigger.Raid, MinAmount = 1, Intensity = 0.9, DurationMs = 4000 },
        new TwitchRule { Name = "New followers", Trigger = TwitchTrigger.Follow, Enabled = false, Intensity = 0.3, DurationMs = 500 },
    };
}
