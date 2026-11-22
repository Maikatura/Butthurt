using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Fansly;
using Butthurt.Fansly.Models;
using Butthurt.Fansly.Services;
using Butthurt.PiShock.Models;
using Butthurt.PiShock.Services;
using Butthurt.PiShock.Views;
using Butthurt.Twitch;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.VRChat;
using Butthurt.VRChat.Services;
using Butthurt.Warcraft;
using Butthurt.Warcraft.Models;
using Butthurt.Warcraft.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.PiShock;

public class PiShockPlugin : IPlugin
{
    public const string PluginId = "PiShock";

    public static PiShockSettings Settings
    {
        get
        {
            var settings = SettingsService.GetPluginSettings<PiShockSettings>(PluginId);
            settings.Rules ??= PiShockSettings.DefaultRules();
            return settings;
        }
    }

    public static bool IsTwitchPluginOn => IsOn(TwitchPlugin.PluginId);
    public static bool IsFanslyPluginOn => IsOn(FanslyPlugin.PluginId);
    public static bool IsWarcraftPluginOn => IsOn(WarcraftPlugin.PluginId);
    public static bool IsVRChatPluginOn => IsOn(VRChatPlugin.PluginId);

    private static bool IsOn(string id) =>
        PluginManager.AllPlugins.FirstOrDefault(p => p.Id == id) is { } plugin && PluginManager.IsEnabled(plugin);

    private bool _active;

    public string Id => PluginId;
    public string Name => "PiShock";
    public PackIconLucideKind Icon => PackIconLucideKind.Zap;
    public SideBarType Category => SideBarType.Hardware;

    public event EventHandler? StatusChanged;

    public PiShockPlugin()
    {
        // PiShock redeem rules can have Butthurt manage their Twitch reward, like VRChat's. Registered here (not in
        // Initialize) so that while this plugin is off its rewards are only hidden, not deleted.
        TwitchService.RegisterRewardSource(() => Settings.Rules!
            .Where(r => r.Source == PiShockSource.Twitch && r.Twitch.Trigger == TwitchTrigger.Redeem && r.Twitch.ManageReward)
            .Select(r => new DesiredReward(r.Twitch.Match, r.Twitch.RewardCost,
                r.Enabled && PluginManager.IsEnabled(this))));

        // The rules live on the platform pages; the PiShock page is only the setup.
        PageSections.Register(TwitchPlugin.PluginId, this, () => new PiShockRulesCard(PiShockSource.Twitch));
        PageSections.Register(FanslyPlugin.PluginId, this, () => new PiShockRulesCard(PiShockSource.Fansly));
        PageSections.Register(WarcraftPlugin.PluginId, this, () => new PiShockRulesCard(PiShockSource.Warcraft));
        PageSections.Register(VRChatPlugin.PluginId, this, () => new PiShockRulesCard(PiShockSource.VRChat));
    }

    public void Initialize()
    {
        _active = true;
        PiShockService.StatusChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        TwitchService.StateChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        FanslyService.StateChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        TwitchService.EventReceived += (_, e) => OnTwitchEvent(e);
        FanslyService.EventReceived += (_, e) => OnFanslyEvent(e);
        // Once per in-game event (not again while ongoing damage keeps the addon's pixel lit).
        WowWatcherService.EventDetected += (_, e) => OnWowEvent(e);
        // Avatar parameters (headpats, boops, toggles…) going on in VRChat.
        VRChatTriggers.ParameterChanged += (_, e) => OnVRChatChange(e);
    }

    /// <summary>Turned off: the subscriptions stay, but nothing happens.</summary>
    private bool Listening => _active && PluginManager.IsEnabled(this);

    private void OnTwitchEvent(TwitchEvent e)
    {
        if (!Listening) return;

        foreach (var rule in Settings.Rules!.ToList())
        {
            if (!PiShockService.Matches(rule, e) || !PiShockService.TryUseCooldown(rule)) continue;
            e.ReportResult(PiShockService.Apply(rule, PiShockService.ScaleFor(rule, e), e.UserName));
        }
    }

    private void OnFanslyEvent(FanslyEvent e)
    {
        if (!Listening) return;

        foreach (var rule in Settings.Rules!.ToList())
        {
            if (!PiShockService.Matches(rule, e) || !PiShockService.TryUseCooldown(rule)) continue;
            e.ReportResult(PiShockService.Apply(rule, PiShockService.ScaleFor(rule, e), e.UserName));
        }
    }

    private void OnWowEvent(WowEventDefinition e)
    {
        if (!Listening) return;

        foreach (var rule in Settings.Rules!.ToList())
        {
            if (!PiShockService.Matches(rule, e) || !PiShockService.TryUseCooldown(rule)) continue;
            PiShockService.Apply(rule, 1, "WoW");
        }
    }

    private void OnVRChatChange(ParameterChange change)
    {
        if (!Listening) return;

        foreach (var rule in Settings.Rules!.ToList())
        {
            if (!PiShockService.Matches(rule, change) || !PiShockService.TryUseCooldown(rule)) continue;
            PiShockService.Apply(rule, 1, "VRChat");
        }
    }

    public Task Start()
    {
        _active = true;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        _active = false;
        PiShockService.StopAll();
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public IEnumerable<IPageView> CreatePages() => new IPageView[] { new PiShockView() };

    public PluginStatus GetStatus()
    {
        if (!PiShockService.HasLogin)
            return new PluginStatus(PluginStatusLevel.Off, "Not set up", "Add your PiShock API key on the PiShock page") { SidebarText = "PiShock not set up" };
        if (!PiShockService.HasShockers)
            return new PluginStatus(PluginStatusLevel.Off, "No shockers", "Press Find my shockers on the PiShock page") { SidebarText = "PiShock: no shockers" };
        if (!_active)
            return new PluginStatus(PluginStatusLevel.Off, "Stopped", "Press Start to react to rules") { SidebarText = "PiShock stopped" };
        if (Settings.Paused)
            return new PluginStatus(PluginStatusLevel.Waiting, "Paused", "Nothing is sent while paused") { SidebarText = "PiShock paused" };

        var sources = new List<string>();
        if (IsTwitchPluginOn && TwitchService.State == TwitchState.Connected) sources.Add("Twitch");
        if (IsFanslyPluginOn && FanslyService.State == FanslyState.Connected) sources.Add("Fansly");
        if (IsWarcraftPluginOn && WowWatcherService.IsRunning) sources.Add("WoW");
        if (IsVRChatPluginOn && VRChatService.IsVRChatRunning) sources.Add("VRChat");

        if (sources.Count == 0)
            return new PluginStatus(PluginStatusLevel.Waiting, "Waiting", "Connect Twitch or Fansly, or start the WoW watcher, so rules get events") { SidebarText = "PiShock waiting" };

        var detail = PiShockService.LastAction != null
            ? $"Last: {PiShockService.LastAction} at {PiShockService.LastActionTime:HH:mm}"
            : $"Ready for {string.Join(" and ", sources)} rules" + (Settings.ShocksAllowed ? "" : " (shocks off)");
        return new PluginStatus(PluginStatusLevel.Active, "Ready", detail) { SidebarText = "PiShock ready" };
    }

    public Control CreateGuideSection() => Ui.Card("PiShock",
        "Let Twitch and Fansly shock, vibrate or beep your PiShock. Every rule says what has to happen and what the shocker does.",
        Ui.NumberedStep(1, "Log in", "Make an API key on login.pishock.com/Account (API Keys → Add New) and paste it on the PiShock page. That's all, no username or password. The key is stored encrypted for your Windows user."),
        Ui.NumberedStep(2, "Add your shockers", "Press Find my shockers on the PiShock page. Give each one a maximum intensity and duration: no rule can go past them. A share code is optional, add one to also use its limits from pishock.com."),
        Ui.NumberedStep(3, "Make rules", "On the Twitch, Fansly, WoW or VRChat page, under PiShock rules: pick what has to happen (bits, a sub, a redeem, a tip, a chat command…) and what the shocker does. Start with vibrations and low intensities, and test everything before going live."),
        Ui.InfoRow("Safety first", "Turn shocks off with one switch (vibrations and beeps keep working), or pause everything. Shock rules can beep first as a warning. The share code's own limits on pishock.com apply too, set them there as a second safety net."),
        Ui.InfoRow("Taking turns", "Commands for one shocker wait for the previous one to finish. At most a few wait in line, the rest are dropped so a flood of events can't pile up."),
        Ui.InfoRow("Channel point rewards", "Twitch redeem rules can let Butthurt create the reward, like VRChat's. A redemption is refunded when nothing could be sent (PiShock not set up, paused or shocks turned off)."));
}
