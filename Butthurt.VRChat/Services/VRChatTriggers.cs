using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Butthurt.Contracts.Services;
using Butthurt.VRChat.Models;

namespace Butthurt.VRChat.Services;

/// <summary>A parameter went past a threshold: its name, the value before and the value now.</summary>
public record ParameterChange(string Name, double Previous, double Value);

/// <summary>
/// VRChat as a source of events: avatar parameters (contacts, toggles, sliders) that go on make the toys buzz
/// (<see cref="VRChatToyRule"/>) or reach other plugins like PiShock through <see cref="ParameterChanged"/>.
/// </summary>
public static class VRChatTriggers
{
    private static readonly Dictionary<string, double> Values = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> LastFired = new();
    private static readonly HashSet<string> Running = new();
    private static bool _initialized;

    /// <summary>Every parameter change (not only rising ones), so other plugins can check their own thresholds.</summary>
    public static event EventHandler<ParameterChange>? ParameterChanged;

    /// <summary>The last time any toy rule went off, for the status line.</summary>
    public static string? LastTrigger { get; private set; }

    private static readonly LinkedList<string> Recent = new();

    /// <summary>
    /// Parameters that changed lately, newest first, so finding a contact's name is "touch it, then click it".
    /// VRChat's own always-changing ones (movement, voice…) are left out.
    /// </summary>
    public static IReadOnlyList<string> RecentlyChanged
    {
        get
        {
            lock (Recent) return Recent.ToList();
        }
    }

    private static readonly HashSet<string> Noisy = new(StringComparer.OrdinalIgnoreCase)
    {
        "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "AngularY", "Upright", "Grounded", "Seated", "AFK",
        "Voice", "Viseme", "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight", "InStation",
        "TrackingType", "VRMode", "MuteSelf", "Earmuffs", "IsLocal", "IsOnFriendsList", "ScaleModified", "ScaleFactor",
        "ScaleFactorInverse", "EyeHeightAsMeters", "EyeHeightAsPercent", "Supine", "GroundProximity", "IsAnimatorEnabled"
    };

    private static List<VRChatToyRule> ToyRules => VRChatPlugin.Settings.ToyRules;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        VRChatService.ParameterReceived += (_, e) => Received(e.Name, e.Value);
    }

    /// <summary>True when the value went from below the threshold to it or above (bools: off to on).</summary>
    public static bool Rises(VRChatParameterCondition condition, ParameterChange change) =>
        !string.IsNullOrWhiteSpace(condition.Parameter) &&
        string.Equals(condition.Parameter.Trim(), change.Name, StringComparison.OrdinalIgnoreCase) &&
        change.Previous < condition.Threshold && change.Value >= condition.Threshold;

    /// <summary>True when off cooldown, and starts the cooldown. Public so PiShock's VRChat rules share it.</summary>
    public static bool TryUseCooldown(VRChatParameterCondition condition)
    {
        lock (LastFired)
        {
            if (condition.CooldownSeconds > 0 && LastFired.TryGetValue(condition.Id, out var last) &&
                DateTime.UtcNow - last < TimeSpan.FromSeconds(condition.CooldownSeconds))
            {
                return false;
            }

            LastFired[condition.Id] = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>The value VRChat last reported for a parameter (0 when never seen).</summary>
    public static double ValueOf(string name)
    {
        lock (Values) return Values.TryGetValue(name.Trim(), out var value) ? value : 0;
    }

    /// <summary>Plays a toy rule as if its parameter went on, for its Test button.</summary>
    public static void Test(VRChatToyRule rule)
    {
        ActivityLog.Add($"Test: {Label(rule)}", LogLevel.Event);
        _ = ButthurtService.Vibrate(rule.Intensity, rule.DurationMs);
    }

    /// <summary>Feeds a value in by hand, the way VRChat would. Public for tests.</summary>
    public static void Received(string name, double value)
    {
        double previous;
        lock (Values)
        {
            previous = Values.TryGetValue(name, out var old) ? old : 0;
            if (Math.Abs(previous - value) < 0.0001) return;
            Values[name] = value;
        }

        if (!Noisy.Contains(name))
        {
            lock (Recent)
            {
                var existing = Recent.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) Recent.Remove(existing);
                Recent.AddFirst(name);
                while (Recent.Count > 8) Recent.RemoveLast();
            }
        }

        var change = new ParameterChange(name, previous, value);

        foreach (var rule in ToyRules.Where(r => r.Enabled).ToList())
        {
            if (!Rises(rule, change) || !TryUseCooldown(rule)) continue;

            LastTrigger = $"{Label(rule)} at {DateTime.Now:HH:mm:ss}";
            ActivityLog.Add($"VRChat: {Label(rule)}", LogLevel.Event);
            _ = ButthurtService.Vibrate(rule.Intensity, rule.DurationMs);
            if (rule.WhileOn) _ = KeepBuzzing(rule);
        }

        ParameterChanged?.Invoke(null, change);
    }

    /// <summary>"While it stays on": buzz again a little before the last buzz runs out, until the parameter goes off.</summary>
    private static async Task KeepBuzzing(VRChatToyRule rule)
    {
        lock (Running)
        {
            if (!Running.Add(rule.Id)) return;
        }

        try
        {
            var gap = TimeSpan.FromMilliseconds(Math.Max(rule.DurationMs * 0.75, 100));
            // Safety net: nothing buzzes for more than 10 minutes on its own, even if VRChat never says "off".
            var until = DateTime.UtcNow + TimeSpan.FromMinutes(10);

            while (DateTime.UtcNow < until)
            {
                await Task.Delay(gap);
                if (!rule.Enabled || !VRChatService.IsRunning || ValueOf(rule.Parameter) < rule.Threshold) break;
                _ = ButthurtService.Vibrate(rule.Intensity, rule.DurationMs);
            }
        }
        finally
        {
            lock (Running) Running.Remove(rule.Id);
        }
    }

    public static string Label(VRChatToyRule rule) =>
        !string.IsNullOrWhiteSpace(rule.Name) ? rule.Name
        : !string.IsNullOrWhiteSpace(rule.Parameter) ? rule.Parameter
        : "Avatar parameter";

    /// <summary>"Headpat goes on" or "Proximity reaches 0.8", plus the cooldown.</summary>
    public static string ConditionSummary(VRChatParameterCondition condition)
    {
        var name = string.IsNullOrWhiteSpace(condition.Parameter) ? "No parameter set" : condition.Parameter.Trim();
        var when = Math.Abs(condition.Threshold - 0.5) < 0.001 ? "goes on" : $"reaches {condition.Threshold:0.##}";
        return $"{name} {when}" + (condition.CooldownSeconds > 0 ? $" · {condition.CooldownSeconds}s cooldown" : "");
    }
}
