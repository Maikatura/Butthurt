using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Fansly;
using Butthurt.Fansly.Models;
using Butthurt.Fansly.Services;
using Butthurt.Twitch;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.VRChat.Models;
using Butthurt.VRChat.Services;
using Butthurt.VRChat.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.VRChat;

public class VRChatPlugin : IPlugin
{
    public const string PluginId = "VRChat";

    public static VRChatSettings Settings
    {
        get
        {
            var settings = SettingsService.GetPluginSettings<VRChatSettings>(PluginId);
            settings.Rules ??= VRChatSettings.DefaultRules();
            return settings;
        }
    }

    /// <summary>Twitch rules only get events while the Twitch plugin is turned on and connected.</summary>
    public static bool IsTwitchPluginOn =>
        PluginManager.AllPlugins.FirstOrDefault(p => p.Id == TwitchPlugin.PluginId) is { } twitch && PluginManager.IsEnabled(twitch);

    public static bool IsFanslyPluginOn =>
        PluginManager.AllPlugins.FirstOrDefault(p => p.Id == FanslyPlugin.PluginId) is { } fansly && PluginManager.IsEnabled(fansly);

    private bool _active;

    public string Id => PluginId;
    public string Name => "VRChat";
    public PackIconLucideKind Icon => PackIconLucideKind.Glasses;

    public event EventHandler? StatusChanged;

    public VRChatPlugin()
    {
        // VRChat redeem rules can have Butthurt manage their reward too. Registered here (not in Initialize)
        // so that while this plugin is turned off its rewards are only hidden, not deleted.
        TwitchService.RegisterRewardSource(() => Settings.Rules!
            .Where(r => r.Source == VRChatSource.Twitch && r.When.Trigger == TwitchTrigger.Redeem && r.When.ManageReward)
            .Select(r => new DesiredReward(r.When.Match, r.When.RewardCost, r.Enabled && PluginManager.IsEnabled(this))));

        // What Twitch and Fansly do in VRChat is set on their pages; this page is the VRChat setup.
        PageSections.Register(TwitchPlugin.PluginId, this, () => new VRChatRulesCard(VRChatSource.Twitch), order: 10);
        PageSections.Register(FanslyPlugin.PluginId, this, () => new VRChatRulesCard(VRChatSource.Fansly), order: 10);
    }

    public void Initialize()
    {
        _active = true;
        VRChatService.StatusChanged += (_, _) =>
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
            Announcer.PushQueue();
        };
        TwitchService.StateChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        TwitchService.EventReceived += (_, e) => OnTwitchEvent(e);
        FanslyService.EventReceived += (_, e) => OnFanslyEvent(e);
        VRChatTriggers.Initialize();

        if (Settings.AutoStart) VRChatService.Start();
    }

    private void OnTwitchEvent(TwitchEvent e)
    {
        // Turned off: the subscription stays, but nothing happens.
        if (!_active) return;

        foreach (var rule in Settings.Rules!.Where(r => r.Enabled && r.Source == VRChatSource.Twitch).ToList())
        {
            if (!TwitchRules.Matches(rule.When, e) || !TwitchRules.TryUseCooldown(rule.When)) continue;

            var worked = VRChatService.Apply(rule);
            e.ReportResult(worked);
            if (worked) Announcer.Announce(rule, e);
        }
    }

    private void OnFanslyEvent(FanslyEvent e)
    {
        if (!_active) return;

        foreach (var rule in Settings.Rules!.Where(r => r.Enabled && r.Source == VRChatSource.Fansly).ToList())
        {
            if (!FanslyRules.Matches(rule.Fansly, e) || !FanslyRules.TryUseCooldown(rule.Fansly)) continue;

            var worked = VRChatService.Apply(rule);
            e.ReportResult(worked);
            if (worked) Announcer.Announce(rule, e);
        }
    }

    /// <summary>Runs a rule as if it matched, for the Test button. Also shows its announcement on the overlay.</summary>
    public static bool Test(VRChatRule rule)
    {
        ActivityLog.Add($"Test: {rule.Name}", LogLevel.Event);
        var worked = VRChatService.Apply(rule);

        // The test shows on the overlay so you can see how it looks, but never goes to chat.
        if (rule.Announce)
        {
            var wait = rule.Action == VRChatAction.ChangeAvatar ? VRChatService.LastSwapWaitSeconds : 0;
            OverlayService.Alert(rule.Source == VRChatSource.Fansly
                ? Announcer.Text(rule, FanslyRules.SampleFor(rule.Fansly), VRChatService.CurrentMultiplier, VRChatService.CurrentAvatarId, wait)
                : Announcer.Text(rule, TwitchRules.SampleFor(rule.When), VRChatService.CurrentMultiplier, VRChatService.CurrentAvatarId, wait));
        }

        return worked;
    }

    public Task Start()
    {
        _active = true;
        VRChatService.Start();
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        _active = false;
        VRChatService.Stop();
        return Task.CompletedTask;
    }

    public IEnumerable<IPageView> CreatePages() => new IPageView[] { new VRChatView() };

    public PluginStatus GetStatus()
    {
        if (!VRChatService.IsRunning)
        {
            return new PluginStatus(PluginStatusLevel.Off, "Stopped", "Not talking to VRChat") { SidebarText = "VRChat stopped" };
        }

        if (!VRChatService.IsVRChatRunning)
        {
            return new PluginStatus(PluginStatusLevel.Waiting, "Waiting", "Start VRChat with OSC turned on") { SidebarText = "Waiting for VRChat" };
        }

        var size = VRChatService.CurrentMultiplier;
        var twitchLive = IsTwitchPluginOn && TwitchService.State == TwitchState.Connected;
        var fanslyLive = IsFanslyPluginOn && FanslyService.State == FanslyState.Connected;
        var detail = Math.Abs(size - 1) > 0.001 ? $"Size ×{size:0.##}, {VRChatService.ActiveEffects.Count} effect(s)"
            : VRChatTriggers.LastTrigger is { } trigger ? $"Last: {trigger}"
            : twitchLive && fanslyLive ? "Ready for Twitch and Fansly rules"
            : twitchLive ? "Ready for Twitch rules"
            : fanslyLive ? "Ready for Fansly rules"
            : "Connect Twitch or Fansly for stream rules";
        return new PluginStatus(PluginStatusLevel.Active, "Connected", detail) { SidebarText = "VRChat connected" };
    }

    public Control CreateGuideSection() => Ui.Card("VRChat",
        "Let Twitch change your avatar and your size. Butthurt talks to VRChat over OSC, VRChat's built-in remote control.",
        Ui.NumberedStep(1, "Turn on OSC in VRChat", "Open the Action Menu → Options → OSC → Enabled. Butthurt sends on port 9000 and listens on 9001, VRChat's defaults."),
        Ui.NumberedStep(2, "Make stream rules", "On the Twitch or Fansly page, under VRChat rules: pick what has to happen (bits, a sub, a tip, a redeem…) and what VRChat should do: switch avatar (optionally switching back later), grow/shrink for a while or change a parameter."),
        Ui.NumberedStep(3, "Let your avatar buzz you", "On the VRChat page, Toy rules and PiShock rules react to your avatar's parameters: a headpat contact, a boop, a toggle. Touch it in game and it shows up under \"Recently changed\" to click."),
        Ui.InfoRow("Stacking", "Grow rules that stack multiply: two ×1.1 grows make you ×1.21. Each one runs out on its own, then you're back to your normal height."),
        Ui.InfoRow("Avatar parameters", "Set or add to any parameter of your avatar (toggles, sliders, ints). Give it a different name per avatar if your avatars call it differently. Whatever is active is sent again after every avatar change, so a redeemed avatar swap doesn't undo it."),
        Ui.InfoRow("Stream overlay", "Turn on the Stream overlay plugin in Settings and add its address as a Browser source in OBS. It shows the queue and an announcement for every rule, with a message you write per rule using placeholders like {user}, {bits} and {multiplier}."),
        Ui.InfoRow("Rewards","Tick \"Let Butthurt create this reward\" on a redeem rule and the reward appears on your channel, follows the rule's cost, hides while the rule is off and is deleted with the rule."),
        Ui.InfoRow("Avatar IDs", "Avatar IDs start with avtr_. Wear the avatar and press Use current avatar, or copy it from the avatar's page on vrchat.com. You can only switch to avatars you're allowed to wear."),
        Ui.InfoRow("Worlds can block scaling", "Some worlds turn avatar scaling off. Growing then does nothing there, avatar changes still work."));
}
