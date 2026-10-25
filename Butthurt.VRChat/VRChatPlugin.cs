using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
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

    /// <summary>The rules only get events while the Twitch plugin is turned on and connected.</summary>
    public static bool IsTwitchPluginOn =>
        PluginManager.AllPlugins.FirstOrDefault(p => p.Id == TwitchPlugin.PluginId) is { } twitch && PluginManager.IsEnabled(twitch);

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
            .Where(r => r.When.Trigger == TwitchTrigger.Redeem && r.When.ManageReward)
            .Select(r => new DesiredReward(r.When.Match, r.When.RewardCost, r.Enabled && PluginManager.IsEnabled(this))));
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

        if (Settings.AutoStart) VRChatService.Start();
    }

    private void OnTwitchEvent(TwitchEvent e)
    {
        // Turned off: the subscription stays, but nothing happens.
        if (!_active) return;

        foreach (var rule in Settings.Rules!.Where(r => r.Enabled))
        {
            if (!TwitchRules.Matches(rule.When, e) || !TwitchRules.TryUseCooldown(rule.When)) continue;

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
            OverlayService.Alert(Announcer.Text(rule, TwitchRules.SampleFor(rule.When), VRChatService.CurrentMultiplier,
                VRChatService.CurrentAvatarId, wait));
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
        var detail = !IsTwitchPluginOn ? "Turn on the Twitch plugin for rules"
            : TwitchService.State != TwitchState.Connected ? "Twitch isn't connected, rules wait for it"
            : Math.Abs(size - 1) > 0.001 ? $"Size ×{size:0.##}, {VRChatService.ActiveEffects.Count} effect(s)"
            : "Ready for Twitch rules";
        return new PluginStatus(PluginStatusLevel.Active, "Connected", detail) { SidebarText = "VRChat connected" };
    }

    public Control CreateGuideSection() => Ui.Card("VRChat",
        "Let Twitch change your avatar and your size. Butthurt talks to VRChat over OSC, VRChat's built-in remote control.",
        Ui.NumberedStep(1, "Turn on OSC in VRChat", "Open the Action Menu → Options → OSC → Enabled. Butthurt sends on port 9000 and listens on 9001, VRChat's defaults."),
        Ui.NumberedStep(2, "Turn on the Twitch plugin", "VRChat rules react to Twitch, so the Twitch plugin has to be on and logged in."),
        Ui.NumberedStep(3, "Make rules", "Pick what has to happen on Twitch and what VRChat should do: switch to an avatar (optionally switching back later) or grow/shrink for a while."),
        Ui.InfoRow("Stacking", "Grow rules that stack multiply: two ×1.1 grows make you ×1.21. Each one runs out on its own, then you're back to your normal height."),
        Ui.InfoRow("Avatar parameters", "Set or add to any parameter of your avatar (toggles, sliders, ints). Give it a different name per avatar if your avatars call it differently. Whatever is active is sent again after every avatar change, so a redeemed avatar swap doesn't undo it."),
        Ui.InfoRow("Stream overlay", "Turn on the OBS overlay on the VRChat page and add its address as a Browser source in OBS. It shows the queue and an announcement for every rule, with a message you write per rule using placeholders like {user}, {bits} and {multiplier}."),
        Ui.InfoRow("Rewards","Tick \"Let Butthurt create this reward\" on a redeem rule and the reward appears on your channel, follows the rule's cost, hides while the rule is off and is deleted with the rule."),
        Ui.InfoRow("Avatar IDs", "Avatar IDs start with avtr_. Wear the avatar and press Use current avatar, or copy it from the avatar's page on vrchat.com. You can only switch to avatars you're allowed to wear."),
        Ui.InfoRow("Worlds can block scaling", "Some worlds turn avatar scaling off. Growing then does nothing there, avatar changes still work."));
}
