using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Butthurt.Contracts.Services;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.VRChat.Models;

namespace Butthurt.VRChat.Services;

/// <summary>
/// Turns a rule and the Twitch event that fired it into the user's announcement text, and sends it
/// to the stream overlay and (optionally) Twitch chat. Also keeps the overlay's queue up to date.
/// </summary>
public static class Announcer
{
    /// <summary>Every placeholder with a short explanation, shown under the template box.</summary>
    public static readonly IReadOnlyList<(string Name, string Meaning)> Placeholders = new[]
    {
        ("user", "who did it"),
        ("event", "what they did, e.g. \"cheered 50 bits\""),
        ("bits", "bits cheered"),
        ("amount", "bits, gifts, raid viewers or sub months"),
        ("bits_min", "the rule's minimum"),
        ("bits_max", "the rule's maximum (∞ when none)"),
        ("tier", "sub tier"),
        ("reward", "redeem name"),
        ("cost", "redeem cost in points"),
        ("message", "their chat message or redeem text"),
        ("multiplier", "this rule's size multiplier"),
        ("size", "your total size after it"),
        ("duration", "seconds it lasts"),
        ("avatar", "avatar name"),
        ("when", "\"now\" or \"in 25s\" when an avatar swap has to wait its turn"),
        ("wait", "seconds until the avatar swap starts"),
        ("parameter", "parameter name"),
        ("value", "parameter value"),
        ("rule", "the rule's name"),
    };

    /// <summary>The placeholder values for a rule firing on an event. Public for the tests and the preview.</summary>
    public static Dictionary<string, string> Values(VRChatRule rule, TwitchEvent e, double sizeAfter, string? avatarId, int waitSeconds = 0)
    {
        var inv = CultureInfo.InvariantCulture;
        var when = rule.When;
        var cost = ManagedCost(when);

        return new Dictionary<string, string>
        {
            ["user"] = e.UserName,
            ["event"] = EventText(e),
            ["bits"] = e.Type == TwitchTrigger.Bits ? e.Amount.ToString(inv) : "0",
            ["amount"] = e.Amount.ToString(inv),
            ["bits_min"] = when.MinAmount.ToString(inv),
            ["bits_max"] = when.MaxAmount > 0 ? when.MaxAmount.ToString(inv) : "∞",
            ["min"] = when.MinAmount.ToString(inv),
            ["max"] = when.MaxAmount > 0 ? when.MaxAmount.ToString(inv) : "∞",
            ["tier"] = e.Tier.ToString(inv),
            ["months"] = e.Amount.ToString(inv),
            ["reward"] = e.RewardTitle,
            ["cost"] = cost,
            ["message"] = e.Message,
            ["multiplier"] = rule.Multiplier.ToString("0.##", inv),
            ["size"] = sizeAfter.ToString("0.##", inv),
            ["duration"] = (rule.Action == VRChatAction.ChangeAvatar ? rule.RevertAfterSeconds : rule.DurationSeconds).ToString(inv),
            ["avatar"] = AvatarLibrary.DisplayName(rule.AvatarId),
            ["wait"] = waitSeconds.ToString(inv),
            ["when"] = waitSeconds > 0 ? $"in {waitSeconds}s" : "now",
            ["parameter"] = rule.ParameterNameFor(avatarId) ?? "",
            ["value"] = rule.ParameterType == ParameterType.Bool
                ? (rule.ParameterValue >= 0.5 ? "on" : "off")
                : rule.ParameterValue.ToString("0.##", inv),
            ["rule"] = rule.Name,
        };
    }

    public static string Text(VRChatRule rule, TwitchEvent e, double sizeAfter, string? avatarId, int waitSeconds = 0) =>
        MessageTemplate.Format(rule.EffectiveAnnouncement, Values(rule, e, sizeAfter, avatarId, waitSeconds));

    /// <summary>Sends the rule's announcement to the overlay and, if turned on, Twitch chat.</summary>
    public static void Announce(VRChatRule rule, TwitchEvent e)
    {
        if (!rule.Announce) return;

        var wait = rule.Action == VRChatAction.ChangeAvatar ? VRChatService.LastSwapWaitSeconds : 0;
        var text = Text(rule, e, VRChatService.CurrentMultiplier, VRChatService.CurrentAvatarId, wait);
        if (string.IsNullOrWhiteSpace(text)) return;

        OverlayService.Alert(text);

        if (VRChatPlugin.Settings.PostAnnouncementsInChat && TwitchService.State == TwitchState.Connected)
        {
            _ = PostInChat(text);
        }
    }

    private static async System.Threading.Tasks.Task PostInChat(string text)
    {
        var error = await TwitchService.SendChatMessage(text);
        if (error != null) ActivityLog.Add($"Couldn't post the announcement in chat: {error}", LogLevel.Warning);
    }

    /// <summary>"cheered 50 bits", "redeemed Grow", "subscribed at tier 2"…</summary>
    private static string EventText(TwitchEvent e) => e.Type switch
    {
        TwitchTrigger.Bits => $"cheered {e.Amount} bits",
        TwitchTrigger.Subscription => e.IsResub ? $"resubscribed at tier {e.Tier}" : $"subscribed at tier {e.Tier}",
        TwitchTrigger.GiftSub => $"gifted {e.Amount} sub{(e.Amount == 1 ? "" : "s")}",
        TwitchTrigger.Redeem => $"redeemed {e.RewardTitle}",
        TwitchTrigger.Follow => "followed",
        TwitchTrigger.Raid => $"raided with {e.Amount} viewers",
        TwitchTrigger.ChatCommand => $"used {e.Message.Split(' ')[0]}",
        _ => "did something"
    };

    private static string ManagedCost(TwitchCondition when) =>
        when.Trigger == TwitchTrigger.Redeem && when.ManageReward ? when.RewardCost.ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>Sends the active effects to the overlay's queue.</summary>
    public static void PushQueue()
    {
        var items = new List<OverlayQueueItem>();

        foreach (var group in VRChatService.ActiveEffects.GroupBy(x => x.RuleId))
        {
            var multiplier = group.Aggregate(1.0, (p, x) => p * x.Multiplier);
            var count = group.Count();
            items.Add(new OverlayQueueItem(
                group.First().Name + (count > 1 ? $" ×{count}" : ""),
                $"Size ×{multiplier:0.##}",
                multiplier >= 1 ? "grow" : "shrink",
                ToUnixMs(group.Max(x => x.ExpiresUtc))));
        }

        foreach (var group in VRChatService.ActiveParameters.GroupBy(x => x.Rule.Id))
        {
            var rule = group.First().Rule;
            items.Add(new OverlayQueueItem(
                rule.Name + (group.Count() > 1 ? $" ×{group.Count()}" : ""),
                rule.ParameterNameFor(VRChatService.CurrentAvatarId) ?? "",
                "parameter",
                ToUnixMs(group.Max(x => x.ExpiresUtc))));
        }

        var queue = VRChatService.AvatarQueue;
        if (VRChatService.CurrentSwap is { } current)
        {
            var then = queue.Count > 0 ? $"Then {AvatarLibrary.DisplayName(queue[0].AvatarId)}"
                : $"Back to {AvatarLibrary.DisplayName(VRChatService.HomeAvatarId)}";
            items.Add(new OverlayQueueItem($"Avatar: {AvatarLibrary.DisplayName(current.Swap.AvatarId)}", then, "avatar",
                ToUnixMs(current.EndsUtc)));
        }

        // Swaps waiting their turn go last, in the order they'll happen.
        var waiting = queue.Select((swap, i) => new OverlayQueueItem(
            $"Up next: {AvatarLibrary.DisplayName(swap.AvatarId)}",
            (i == 0 ? "Next" : $"#{i + 1} in line") + (swap.Seconds > 0 ? $" · {swap.Seconds}s" : " · keeps it"),
            "avatar-next", null));

        OverlayService.SetQueue(items.OrderBy(i => i.ExpiresAtUnixMs ?? long.MaxValue).Concat(waiting));
    }

    private static long? ToUnixMs(DateTime utc) =>
        utc == DateTime.MaxValue ? null : new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
