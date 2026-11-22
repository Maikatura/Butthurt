using System;
using System.Collections.Generic;
using System.Linq;
using Butthurt.Fansly.Models;

namespace Butthurt.Fansly.Services;

/// <summary>Decides which rules fire for an event. No Fansly connection needed, so it is easy to test.</summary>
public static class FanslyRules
{
    private const double MaxScale = 5;

    private static readonly Dictionary<string, DateTime> LastFired = new();

    public static bool Matches(FanslyCondition rule, FanslyEvent e)
    {
        if (!rule.Enabled || rule.Trigger != e.Type) return false;

        return e.Type switch
        {
            FanslyTrigger.Tip => e.TipDollars >= rule.MinTip - 0.001 && (rule.MaxTip <= 0 || e.TipDollars <= rule.MaxTip + 0.001),
            FanslyTrigger.Subscription => TierMatches(rule, e.TierName) && (e.Months <= 1 || rule.IncludeResubs),
            FanslyTrigger.ChatCommand => !string.IsNullOrWhiteSpace(rule.Match) &&
                                         e.Message.TrimStart().StartsWith(rule.Match.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                         e.ChatterLevel >= rule.WhoCanUse,
            _ => false
        };
    }

    /// <summary>True when the condition is off cooldown, and starts the cooldown. Call it only when acting on the event.</summary>
    public static bool TryUseCooldown(FanslyCondition rule)
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

    /// <summary>How much bigger than the minimum the event is (1 to 5), for rules that scale with the tip.</summary>
    public static double ScaleFor(FanslyCondition rule, FanslyEvent e) =>
        !rule.Trigger.HasAmount() ? 1 : Math.Clamp(e.TipDollars / Math.Max(rule.MinTip, 0.01), 1, MaxScale);

    public static int DurationFor(FanslyRule rule, FanslyEvent e) =>
        rule.ScaleWithAmount ? (int)(rule.DurationMs * ScaleFor(rule, e)) : rule.DurationMs;

    /// <summary>A made-up event that this condition matches, for the Test buttons.</summary>
    public static FanslyEvent SampleFor(FanslyCondition rule) => new(rule.Trigger, "TestUser")
    {
        TipDollars = Math.Max(rule.MinTip, 1),
        TierName = rule.Tiers.FirstOrDefault() ?? "Test tier",
        Message = rule.Match,
        ChatterLevel = FanslyChatterLevel.Creator
    };

    public static string Describe(FanslyEvent e) => e.Type switch
    {
        FanslyTrigger.Tip => $"{e.UserName} tipped ${e.TipDollars:0.00}" + (e.Message.Length > 0 ? $": {Shorten(e.Message)}" : ""),
        FanslyTrigger.Subscription => e.Months > 1
            ? $"{e.UserName} resubscribed{TierSuffix(e)} ({e.Months} months)"
            : $"{e.UserName} subscribed{TierSuffix(e)}",
        FanslyTrigger.ChatCommand => $"{e.UserName}: {Shorten(e.Message)}",
        _ => e.Type.ToString()
    };

    public static string ConditionSummary(FanslyCondition rule)
    {
        var condition = rule.Trigger switch
        {
            FanslyTrigger.Tip => rule.MaxTip > 0 ? $"${rule.MinTip:0.##}–${rule.MaxTip:0.##} tips" : $"${rule.MinTip:0.##}+ tips",
            FanslyTrigger.Subscription => (rule.Tiers.Count == 0 ? "Any tier" : string.Join(", ", rule.Tiers)) +
                                          (rule.IncludeResubs ? " · new subs & resubs" : " · new subs only"),
            FanslyTrigger.ChatCommand => string.IsNullOrWhiteSpace(rule.Match)
                ? "No command set"
                : $"\"{rule.Match}\" · {rule.WhoCanUse.ToString().ToLowerInvariant()}",
            _ => ""
        };

        return condition + (rule.CooldownSeconds > 0 ? $" · {rule.CooldownSeconds}s cooldown" : "");
    }

    public static string Summary(FanslyRule rule)
    {
        var duration = rule.DurationMs < 1000 ? $"{rule.DurationMs} ms" : $"{rule.DurationMs / 1000.0:0.#} s";
        var scales = rule.ScaleWithAmount && rule.Trigger.HasAmount() ? " · scales" : "";
        return $"{ConditionSummary(rule)} → {rule.Intensity * 100:0}% for {duration}{scales}";
    }

    private static bool TierMatches(FanslyCondition rule, string tier) =>
        rule.Tiers.Count == 0 || rule.Tiers.Any(t => string.Equals(t.Trim(), tier.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string TierSuffix(FanslyEvent e) => e.TierName.Length > 0 ? $" ({e.TierName})" : "";

    private static string Shorten(string text) => text.Length > 80 ? text[..80] + "…" : text;
}
