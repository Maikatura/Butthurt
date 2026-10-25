using System;
using System.Collections.Generic;
using System.Linq;
using Butthurt.Twitch.Models;

namespace Butthurt.VRChat.Models;

public enum VRChatAction
{
    /// <summary>Switch to another avatar, optionally switching back after a while.</summary>
    ChangeAvatar,

    /// <summary>Multiply your height for a while. Several of these can stack.</summary>
    Grow,

    /// <summary>Set (or add to) an avatar parameter, kept through avatar changes while it lasts.</summary>
    SetParameter
}

public enum ParameterType
{
    Bool,
    Int,
    Float
}

public enum ParameterMode
{
    /// <summary>Set the parameter to the value. The newest active one wins.</summary>
    Set,

    /// <summary>Add the value on top (floats and ints), so several triggers stack.</summary>
    Add
}

/// <summary>
/// Which parameter to use on which avatar. Avatars name the same thing differently, so a rule can have a
/// name per avatar plus a fallback (empty AvatarId) for every other avatar.
/// </summary>
public class ParameterTarget
{
    /// <summary>Empty: any avatar without its own entry.</summary>
    public string AvatarId { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>"When this happens on Twitch, do this in VRChat."</summary>
public class VRChatRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>The same conditions Twitch's own rules use (bits range, tiers, reward name, cooldown…).</summary>
    public TwitchCondition When { get; set; } = new() { Trigger = TwitchTrigger.Bits, MinAmount = 20 };

    public VRChatAction Action { get; set; }

    // Change avatar
    public string AvatarId { get; set; } = "";

    /// <summary>Switch back to the avatar you had before after this many seconds. 0 keeps the new one.</summary>
    public int RevertAfterSeconds { get; set; }

    // Grow (a multiplier below 1 shrinks)
    public double Multiplier { get; set; } = 1.1;
    public int DurationSeconds { get; set; } = 30;

    /// <summary>True: every trigger adds another multiplier (1.1 × 1.1 = 1.21). False: it only restarts the timer.</summary>
    public bool Stack { get; set; } = true;

    // Set parameter (uses DurationSeconds too, 0 = until you reset it)
    public List<ParameterTarget> Parameters { get; set; } = new() { new ParameterTarget() };
    public ParameterType ParameterType { get; set; } = ParameterType.Float;
    public ParameterMode ParameterMode { get; set; } = ParameterMode.Set;
    public double ParameterValue { get; set; } = 1;

    /// <summary>What the parameter goes back to when the effect ends (or is reset).</summary>
    public double ParameterResetValue { get; set; }

    // Announcement on the stream overlay (and optionally in Twitch chat) when the rule fires.
    public bool Announce { get; set; } = true;

    /// <summary>The message with {placeholders}. Empty uses <see cref="DefaultAnnouncement"/>.</summary>
    public string AnnouncementTemplate { get; set; } = "";

    public string EffectiveAnnouncement => string.IsNullOrWhiteSpace(AnnouncementTemplate) ? DefaultAnnouncement(Action) : AnnouncementTemplate;

    public static string DefaultAnnouncement(VRChatAction action) => action switch
    {
        VRChatAction.ChangeAvatar => "{user} {event}: turning into {avatar} {when}!",
        VRChatAction.SetParameter => "{user} {event}: {parameter} for {duration}s!",
        _ => "{user} {event}: ×{multiplier} for {duration}s (now ×{size})!"
    };

    /// <summary>The parameter name to use on the given avatar: its own entry, else the "any avatar" one.</summary>
    public string? ParameterNameFor(string? avatarId)
    {
        var specific = Parameters.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.AvatarId) && string.Equals(p.AvatarId.Trim(), avatarId, StringComparison.OrdinalIgnoreCase));
        var name = (specific ?? Parameters.FirstOrDefault(p => string.IsNullOrWhiteSpace(p.AvatarId)))?.Name.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}

public class VRChatSettings
{
    public string OscHost { get; set; } = "127.0.0.1";

    /// <summary>VRChat listens here (its default is 9000).</summary>
    public int SendPort { get; set; } = 9000;

    /// <summary>VRChat talks back here (its default is 9001). Only one program can listen on it.</summary>
    public int ListenPort { get; set; } = 9001;

    public bool AutoStart { get; set; } = true;

    /// <summary>Your normal eye height in meters. Growing multiplies this.</summary>
    public double BaseEyeHeight { get; set; } = 1.6;

    /// <summary>How long growing or shrinking takes to slide to the new size. 0 jumps instantly.</summary>
    public double SmoothSeconds { get; set; } = 1.5;

    /// <summary>Pick up changes you make in VRChat's own scaling menu as your new normal height.</summary>
    public bool LearnBaseHeight { get; set; } = true;

    public double MinMultiplier { get; set; } = 0.1;
    public double MaxMultiplier { get; set; } = 10;

    /// <summary>Used to switch back when Butthurt doesn't know which avatar you had before.</summary>
    public string DefaultAvatarId { get; set; } = "";

    /// <summary>Also post rule announcements in your Twitch chat (needs the chat permission).</summary>
    public bool PostAnnouncementsInChat { get; set; }

    public List<VRChatRule>? Rules { get; set; }

    /// <summary>The example from the readme: grow for 20 bits, avatar A for 50, avatar B for 300.</summary>
    public static List<VRChatRule> DefaultRules() => new()
    {
        new VRChatRule
        {
            Name = "Grow a little", Action = VRChatAction.Grow, Multiplier = 1.1, DurationSeconds = 30, Stack = true,
            When = new TwitchCondition { Trigger = TwitchTrigger.Bits, MinAmount = 20, MaxAmount = 49 }
        },
        new VRChatRule
        {
            Name = "Avatar for 50 bits", Action = VRChatAction.ChangeAvatar, Enabled = false,
            When = new TwitchCondition { Trigger = TwitchTrigger.Bits, MinAmount = 50, MaxAmount = 299 }
        },
        new VRChatRule
        {
            Name = "Avatar for 300 bits", Action = VRChatAction.ChangeAvatar, Enabled = false,
            When = new TwitchCondition { Trigger = TwitchTrigger.Bits, MinAmount = 300 }
        },
    };
}
