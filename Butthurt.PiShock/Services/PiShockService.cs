using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Butthurt.Contracts.Services;
using Butthurt.Fansly.Models;
using Butthurt.Fansly.Services;
using Butthurt.PiShock.Models;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.VRChat.Models;
using Butthurt.VRChat.Services;
using Butthurt.Warcraft.Models;
using Butthurt.Warcraft.Views;

namespace Butthurt.PiShock.Services;

/// <summary>
/// Turns rules into PiShock commands (sent by <see cref="PiShockClient"/>), with the safety limits on top.
///
/// Commands for one shocker take turns: the next one is sent once the previous one has finished, so overlapping
/// events don't cut each other off or arrive all at once. The queue is short on purpose, extra ones are dropped.
/// </summary>
public static class PiShockService
{

    /// <summary>Most commands one shocker may have waiting. More than this are dropped, not saved up.</summary>
    private const int MaxQueued = 5;

    private const double MaxScale = 5;

    private static readonly Dictionary<string, ShockerQueue> Queues = new();
    private static readonly Dictionary<string, DateTime> LastFired = new();

    // Replaced by StopAll, so everything still waiting gives up.
    private static CancellationTokenSource _stop = new();

    private sealed class ShockerQueue
    {
        public readonly SemaphoreSlim Turn = new(1, 1);
        public int Pending;
    }

    public static string? LastError { get; private set; }
    public static string? LastAction { get; private set; }
    public static DateTime LastActionTime { get; private set; }

    /// <summary>Raised after every command (sent or failed) and when settings that change the status change.</summary>
    public static event EventHandler? StatusChanged;

    private static PiShockSettings Settings => PiShockPlugin.Settings;

    public static bool HasLogin => !string.IsNullOrEmpty(ApiKey);
    public static bool HasShockers => Settings.Shockers.Any(s => s.Enabled && s.HasRoute);
    public static bool IsReady => HasLogin && HasShockers && !Settings.Paused;

    private static string? ApiKey => Secrets.Unprotect(Settings.ApiKey);

    public static void SetApiKey(string? key)
    {
        key = key?.Trim();
        Settings.ApiKey = string.IsNullOrEmpty(key) ? null : Secrets.Protect(key);
        Settings.Username = "";
        LastError = null;
        PiShockClient.ResetLogin();
        SettingsService.Save();
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Asks PiShock whose key it is. Returns an error message, or null when the key works.</summary>
    public static async Task<string?> CheckLogin()
    {
        if (!HasLogin) return "add your PiShock API key first";
        var error = await PiShockClient.CheckLogin(ApiKey!);
        LastError = error == null ? null : $"PiShock: {error}";
        StatusChanged?.Invoke(null, EventArgs.Empty);
        return error;
    }

    /// <summary>
    /// Adds every shocker on the account that isn't in the list yet. Returns how many were added, or an error message.
    /// </summary>
    public static async Task<(int Added, string? Error)> FindMyShockers()
    {
        if (!HasLogin) return (0, "Add your PiShock API key first.");

        var (devices, error) = await PiShockClient.GetMyShockers(ApiKey!);
        if (error != null) return (0, error);
        if (devices.Count == 0) return (0, "PiShock says there are no shockers on this account.");

        var added = 0;
        foreach (var device in devices)
        {
            if (Settings.Shockers.Any(s => s.ShockerId == device.ShockerId && s.HubId == device.HubId)) continue;

            Settings.Shockers.Add(new PiShockShocker
            {
                Name = device.Name.Length > 0 ? device.Name : $"Shocker {device.ShockerId}",
                HubId = device.HubId,
                ShockerId = device.ShockerId
            });
            added++;
        }

        SettingsService.Save();
        StatusChanged?.Invoke(null, EventArgs.Empty);
        return (added, null);
    }

    #region Rules

    public static bool Matches(PiShockRule rule, TwitchEvent e) =>
        rule.Enabled && rule.Source == PiShockSource.Twitch && TwitchRules.Matches(rule.Twitch, e);

    public static bool Matches(PiShockRule rule, FanslyEvent e) =>
        rule.Enabled && rule.Source == PiShockSource.Fansly && FanslyRules.Matches(rule.Fansly, e);

    public static bool Matches(PiShockRule rule, WowEventDefinition e) =>
        rule.Enabled && rule.Source == PiShockSource.Warcraft && rule.Warcraft.Event == e.Type;

    public static bool Matches(PiShockRule rule, ParameterChange change) =>
        rule.Enabled && rule.Source == PiShockSource.VRChat && VRChatTriggers.Rises(rule.VRChat, change);

    /// <summary>
    /// The rule's cooldown. Kept apart from Twitch's and Fansly's own (the condition's id is the key there too,
    /// but a PiShock rule is its own thing). True when it may fire, and starts the cooldown.
    /// </summary>
    public static bool TryUseCooldown(PiShockRule rule)
    {
        var seconds = rule.Source switch
        {
            PiShockSource.Twitch => rule.Twitch.CooldownSeconds,
            PiShockSource.Fansly => rule.Fansly.CooldownSeconds,
            PiShockSource.VRChat => rule.VRChat.CooldownSeconds,
            _ => rule.Warcraft.CooldownSeconds
        };
        lock (LastFired)
        {
            if (seconds > 0 && LastFired.TryGetValue(rule.Id, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(seconds))
            {
                return false;
            }

            LastFired[rule.Id] = DateTime.UtcNow;
            return true;
        }
    }

    public static double ScaleFor(PiShockRule rule, TwitchEvent e) =>
        rule.ScaleWithAmount && rule.Twitch.Trigger.HasAmount()
            ? Math.Clamp(e.Amount / (double)Math.Max(rule.Twitch.MinAmount, 1), 1, MaxScale)
            : 1;

    public static double ScaleFor(PiShockRule rule, FanslyEvent e) =>
        rule.ScaleWithAmount ? FanslyRules.ScaleFor(rule.Fansly, e) : 1;

    /// <summary>
    /// Sends the rule's command to its shockers. True when at least one command was queued; false when nothing
    /// could happen (not set up, paused, shocks turned off, no shocker), which refunds Butthurt's own Twitch rewards.
    /// </summary>
    public static bool Apply(PiShockRule rule, double scale, string who)
    {
        if (!HasLogin)
        {
            Fail($"PiShock: skipped {Label(rule)}, add your PiShock API key first");
            return false;
        }

        if (Settings.Paused)
        {
            ActivityLog.Add($"PiShock: skipped {Label(rule)}, PiShock is paused", LogLevel.Warning);
            return false;
        }

        if (rule.Op == PiShockOp.Shock && !Settings.ShocksAllowed)
        {
            ActivityLog.Add($"PiShock: skipped {Label(rule)}, shocks are turned off", LogLevel.Warning);
            return false;
        }

        var targets = TargetsFor(rule);
        if (targets.Count == 0)
        {
            Fail($"PiShock: skipped {Label(rule)}, no shocker to send it to");
            return false;
        }

        var duration = (int)Math.Round(rule.DurationSeconds * Math.Max(scale, 1));
        var queued = false;
        foreach (var shocker in targets)
        {
            queued |= Enqueue(shocker, rule.Op, rule.Intensity, duration, who, Label(rule), rule.WarnFirst);
        }

        return queued;
    }

    /// <summary>The rule as if it matched, for the Test button.</summary>
    public static bool Test(PiShockRule rule)
    {
        ActivityLog.Add($"Test: {Label(rule)}", LogLevel.Event);
        return Apply(rule, 1, "Test");
    }

    /// <summary>One short command to one shocker, for the shocker's own test buttons. Returns an error message or null.</summary>
    public static string? TestShocker(PiShockShocker shocker, PiShockOp op)
    {
        if (!HasLogin) return "Add your PiShock API key first.";
        if (!shocker.HasRoute) return "This shocker isn't linked to PiShock yet. Use Find my shockers, or add a share code.";
        if (Settings.Paused) return "PiShock is paused.";
        if (op == PiShockOp.Shock && !Settings.ShocksAllowed) return "Shocks are turned off.";

        // Tests are gentle on purpose: the lowest of 10% and the shocker's maximum, for one second.
        var intensity = Math.Min(10, shocker.MaxIntensity);
        return Enqueue(shocker, op, intensity, 1, "Test", $"Test {op.DisplayName().ToLowerInvariant()}", false)
            ? null
            : "This shocker is busy, try again in a moment.";
    }

    /// <summary>Drops every command still waiting. One that's already running finishes (PiShock can't cancel it).</summary>
    public static void StopAll()
    {
        var old = Interlocked.Exchange(ref _stop, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
        PiShockClient.CloseBroker();
    }

    private static List<PiShockShocker> TargetsFor(PiShockRule rule)
    {
        var usable = Settings.Shockers.Where(s => s.Enabled && s.HasRoute).ToList();
        return rule.Target switch
        {
            ShockerTarget.One => usable.Where(s => s.Id == rule.ShockerId).ToList(),
            ShockerTarget.Random when usable.Count > 0 => new List<PiShockShocker> { usable[System.Random.Shared.Next(usable.Count)] },
            _ => usable
        };
    }

    #endregion

    #region Sending

    private static bool Enqueue(PiShockShocker shocker, PiShockOp op, int intensity, int durationSeconds, string who, string what, bool warnFirst)
    {
        // The shocker's limits always win over the rule.
        intensity = Math.Clamp(intensity, 1, Math.Clamp(shocker.MaxIntensity, 1, 100));
        durationSeconds = Math.Clamp(durationSeconds, 1, Math.Clamp(shocker.MaxDurationSeconds, 1, 15));

        ShockerQueue queue;
        lock (Queues)
        {
            if (!Queues.TryGetValue(shocker.Id, out queue!)) Queues[shocker.Id] = queue = new ShockerQueue();
            if (queue.Pending >= MaxQueued)
            {
                ActivityLog.Add($"PiShock: {shocker.Name} is busy, dropped {what}", LogLevel.Warning);
                return false;
            }

            queue.Pending++;
        }

        var stop = _stop.Token;
        _ = Task.Run(async () =>
        {
            var hasTurn = false;
            try
            {
                await queue.Turn.WaitAsync(stop);
                hasTurn = true;

                // Settings can change while waiting in line.
                if (Settings.Paused || (op == PiShockOp.Shock && !Settings.ShocksAllowed)) return;

                if (op == PiShockOp.Shock && warnFirst)
                {
                    await Send(shocker, PiShockOp.Beep, 0, 1, who, $"warning for {what}");
                    await Task.Delay(TimeSpan.FromSeconds(1.5), stop);
                }

                if (await Send(shocker, op, intensity, durationSeconds, who, what))
                {
                    // Let it finish before the next one in line starts.
                    await Task.Delay(TimeSpan.FromSeconds(durationSeconds + 0.3), stop);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped or paused.
            }
            finally
            {
                if (hasTurn) queue.Turn.Release();
                lock (Queues) queue.Pending--;
            }
        });

        return true;
    }

    /// <summary>One command. True when PiShock accepted it.</summary>
    private static async Task<bool> Send(PiShockShocker shocker, PiShockOp op, int intensity, int durationSeconds, string who, string what)
    {
        var name = string.IsNullOrWhiteSpace(Settings.SenderName) ? "Butthurt" : Settings.SenderName.Trim();
        if (!string.IsNullOrWhiteSpace(who)) name = $"{name} ({who})";
        if (name.Length > 60) name = name[..60];

        var description = op == PiShockOp.Beep
            ? $"beep {durationSeconds}s"
            : $"{op.DisplayName().ToLowerInvariant()} {intensity}% {durationSeconds}s";

        if (await PiShockClient.Operate(shocker, op, intensity, durationSeconds, name) is { } error)
        {
            Fail($"PiShock: {what} on {shocker.Name} failed: {error}");

            // Tests run in the background; whoever pressed the button should see why it didn't work.
            if (who == "Test") Notify($"{what} on {shocker.Name} failed: {error}");
            return false;
        }

        if (who == "Test") Notify($"{what} sent to {shocker.Name}.");

        LastError = null;
        LastAction = $"{what}: {description} on {shocker.Name}";
        LastActionTime = DateTime.Now;
        ActivityLog.Add($"PiShock: {LastAction}", LogLevel.Success);
        StatusChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    private static void Notify(string message)
    {
        try
        {
            _ = NotificationService.ShowAsync(message, 6000);
        }
        catch
        {
            // No window to show it in (tests), the activity log has it.
        }
    }

    private static void Fail(string message)
    {
        LastError = message;
        ActivityLog.Add(message, LogLevel.Error);
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void NotifyStatusChanged() => StatusChanged?.Invoke(null, EventArgs.Empty);

    private static string Label(PiShockRule rule) => string.IsNullOrWhiteSpace(rule.Name) ? rule.Op.DisplayName() : rule.Name;

    #endregion

    #region Summaries

    public static string ConditionSummary(PiShockRule rule) => rule.Source switch
    {
        PiShockSource.Twitch => $"{rule.Twitch.Trigger.DisplayName()} · {TwitchRules.ConditionSummary(rule.Twitch)}",
        PiShockSource.Fansly => $"{rule.Fansly.Trigger.DisplayName()} · {FanslyRules.ConditionSummary(rule.Fansly)}",
        PiShockSource.VRChat => VRChatTriggers.ConditionSummary(rule.VRChat),
        _ => WowConditionEditor.Summary(rule.Warcraft)
    };

    public static string Summary(PiShockRule rule)
    {
        var action = rule.Op == PiShockOp.Beep
            ? $"Beep {rule.DurationSeconds}s"
            : $"{rule.Op.DisplayName()} {rule.Intensity}% for {rule.DurationSeconds}s";

        var target = rule.Target switch
        {
            ShockerTarget.One => Settings.Shockers.FirstOrDefault(s => s.Id == rule.ShockerId)?.Name is { Length: > 0 } n ? n : "no shocker picked",
            ShockerTarget.Random => "a random shocker",
            _ => "every shocker"
        };

        var extras = (rule.ScaleWithAmount ? " · scales" : "") + (rule.Op == PiShockOp.Shock && rule.WarnFirst ? " · beeps first" : "");
        return $"{ConditionSummary(rule)} → {action} on {target}{extras}";
    }

    #endregion
}
