using System;
using System.Collections.Generic;
using Butthurt.Contracts.Services;
using Butthurt.Twitch.Models;

namespace Butthurt.Twitch.Services;

/// <summary>Decides which rules fire for an event. No Twitch connection needed, so it is easy to test.</summary>
public static class TwitchRules
{
    private const double MaxScale = 5;

    private static readonly Dictionary<string, DateTime> LastFired = new();

    public static bool Matches(TwitchCondition rule, TwitchEvent e)
    {
        if (!rule.Enabled || rule.Trigger != e.Type) return false;

        return e.Type switch
        {
            TwitchTrigger.Bits or TwitchTrigger.Raid => InRange(rule, e.Amount),
            TwitchTrigger.GiftSub => InRange(rule, e.Amount) && rule.Tiers.Contains(e.Tier),
            TwitchTrigger.Subscription => rule.Tiers.Contains(e.Tier) && (!e.IsResub || rule.IncludeResubs),
            // An empty reward name means "any redeem".
            TwitchTrigger.Redeem => string.IsNullOrWhiteSpace(rule.Match) ||
                                    string.Equals(rule.Match.Trim(), e.RewardTitle.Trim(), StringComparison.OrdinalIgnoreCase),
            TwitchTrigger.ChatCommand => !string.IsNullOrWhiteSpace(rule.Match) &&
                                         e.Message.TrimStart().StartsWith(rule.Match.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                         e.ChatterLevel >= rule.WhoCanUse,
            TwitchTrigger.Follow => true,
            _ => false
        };
    }

    /// <summary>
    /// True when the condition is off cooldown, and starts the cooldown. Call it only once you are
    /// actually going to act on the event.
    /// </summary>
    public static bool TryUseCooldown(TwitchCondition rule)
    {
        lock (LastFired)
        {
            if (rule.CooldownSeconds > 0 && LastFired.TryGetValue(rule.Id, out var last) &&
                DateTime.UtcNow - last < TimeSpan.FromSeconds(rule.CooldownSeconds))
            {
                return false;
            }

            LastFired[rule.Id] = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>The rule's duration, stretched for bigger amounts when the rule asks for it.</summary>
    public static int DurationFor(TwitchRule rule, TwitchEvent e)
    {
        if (!rule.ScaleWithAmount || !rule.Trigger.HasAmount()) return rule.DurationMs;

        var scale = Math.Clamp(e.Amount / (double)Math.Max(rule.MinAmount, 1), 1, MaxScale);
        return (int)(rule.DurationMs * scale);
    }

    /// <summary>A made-up event that this condition matches, for the Test buttons.</summary>
    public static TwitchEvent SampleFor(TwitchCondition rule) => new(rule.Trigger, "TestUser")
    {
        Amount = Math.Max(rule.MinAmount, 1),
        Tier = rule.Tiers.Count > 0 ? rule.Tiers[0] : 1,
        RewardTitle = string.IsNullOrWhiteSpace(rule.Match) ? "Test reward" : rule.Match,
        Message = rule.Match,
        ChatterLevel = ChatterLevel.Moderators
    };

    public static string Describe(TwitchEvent e) => e.Type switch
    {
        TwitchTrigger.Bits => $"{e.UserName} cheered {e.Amount} bits",
        TwitchTrigger.Subscription => e.IsResub
            ? $"{e.UserName} resubscribed at tier {e.Tier} ({e.Amount} months)"
            : $"{e.UserName} subscribed at tier {e.Tier}",
        TwitchTrigger.GiftSub => $"{e.UserName} gifted {e.Amount} tier {e.Tier} sub{(e.Amount == 1 ? "" : "s")}",
        TwitchTrigger.Redeem => $"{e.UserName} redeemed {e.RewardTitle}",
        TwitchTrigger.Follow => $"{e.UserName} followed",
        TwitchTrigger.Raid => $"{e.UserName} raided with {e.Amount} viewers",
        TwitchTrigger.ChatCommand => $"{e.UserName}: {e.Message}",
        _ => e.Type.ToString()
    };

    /// <summary>The "when" part of a summary, e.g. "500+ bits" or "Tier 2, 3 · new subs only".</summary>
    public static string ConditionSummary(TwitchCondition rule)
    {
        var condition = rule.Trigger switch
        {
            TwitchTrigger.Bits => Range(rule, "bits"),
            TwitchTrigger.Raid => Range(rule, "viewers"),
            TwitchTrigger.GiftSub => $"{Range(rule, "gifts")} · {Tiers(rule)}",
            TwitchTrigger.Subscription => Tiers(rule) + (rule.IncludeResubs ? " · new subs & resubs" : " · new subs only"),
            TwitchTrigger.Redeem => string.IsNullOrWhiteSpace(rule.Match) ? "Any reward" : $"\"{rule.Match}\"",
            TwitchTrigger.ChatCommand => string.IsNullOrWhiteSpace(rule.Match)
                ? "No command set"
                : $"\"{rule.Match}\" · {rule.WhoCanUse.ToString().ToLowerInvariant()}",
            _ => "Every follow"
        };

        return condition + (rule.CooldownSeconds > 0 ? $" · {rule.CooldownSeconds}s cooldown" : "");
    }

    /// <summary>One line summary shown under a vibration rule's name.</summary>
    public static string Summary(TwitchRule rule)
    {
        var duration = rule.DurationMs < 1000 ? $"{rule.DurationMs} ms" : $"{rule.DurationMs / 1000.0:0.#} s";
        var scales = rule.ScaleWithAmount && rule.Trigger.HasAmount() ? " · scales" : "";
        return $"{ConditionSummary(rule)} → {rule.Intensity * 100:0}% for {duration}{scales}";
    }

    private static bool InRange(TwitchCondition rule, int amount) =>
        amount >= rule.MinAmount && (rule.MaxAmount <= 0 || amount <= rule.MaxAmount);

    private static string Range(TwitchCondition rule, string unit) =>
        rule.MaxAmount > 0 ? $"{rule.MinAmount}–{rule.MaxAmount} {unit}" : $"{rule.MinAmount}+ {unit}";

    private static string Tiers(TwitchCondition rule) =>
        rule.Tiers.Count == 0 ? "No tiers" : "Tier " + string.Join(", ", rule.Tiers);
}
