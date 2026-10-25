using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Twitch.Models;
using Butthurt.Twitch.Services;
using Butthurt.Twitch.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Twitch;

public class TwitchPlugin : IPlugin
{
    public const string PluginId = "Twitch";

    public static TwitchSettings Settings
    {
        get
        {
            var settings = SettingsService.GetPluginSettings<TwitchSettings>(PluginId);
            settings.Rules ??= TwitchTriggers.DefaultRules();
            return settings;
        }
    }

    private TwitchEvent? _lastEvent;
    private DateTime _lastEventTime;

    public string Id => PluginId;
    public string Name => "Twitch";
    public PackIconLucideKind Icon => PackIconLucideKind.Twitch;

    public event EventHandler? StatusChanged;

    public TwitchPlugin()
    {
        // Redeem rules with "let Butthurt manage this reward" ask for their reward to exist on Twitch.
        TwitchService.RegisterRewardSource(() => Settings.Rules!
            .Where(r => r.Trigger == TwitchTrigger.Redeem && r.ManageReward)
            .Select(r => new DesiredReward(r.Match, r.RewardCost, r.Enabled)));
    }

    public void Initialize()
    {
        // Any rule change (new rule, renamed reward, rule turned off…) might mean a reward has to change.
        SettingsService.Changed += (_, _) => TwitchService.RequestRewardSync();

        TwitchService.StateChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        TwitchService.EventReceived += (_, e) =>
        {
            if (e.Type == TwitchTrigger.ChatCommand) return;
            _lastEvent = e;
            _lastEventTime = DateTime.Now;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        };

        if (Settings.AutoConnect && TwitchService.IsLoggedIn) _ = TwitchService.Connect();
    }

    public Task Start() => TwitchService.IsLoggedIn ? TwitchService.Connect() : Task.CompletedTask;

    public Task Stop() => TwitchService.Disconnect();

    public IEnumerable<IPageView> CreatePages() => new IPageView[] { new TwitchView() };

    public PluginStatus GetStatus() => TwitchService.State switch
    {
        TwitchState.Connected => new PluginStatus(PluginStatusLevel.Active, "Live",
            _lastEvent != null
                ? $"Last: {TwitchRules.Describe(_lastEvent)} at {_lastEventTime:HH:mm}"
                : $"Listening to twitch.tv/{TwitchService.ChannelName}") { SidebarText = "Twitch connected" },
        TwitchState.Connecting => new PluginStatus(PluginStatusLevel.Waiting, "Connecting", "Talking to Twitch…") { SidebarText = "Twitch connecting…" },
        TwitchState.WaitingForLogin => new PluginStatus(PluginStatusLevel.Waiting, "Log in", "Finish logging in in your browser") { SidebarText = "Twitch login…" },
        _ when TwitchService.IsLoggedIn => new PluginStatus(PluginStatusLevel.Off, "Offline",
            TwitchService.LastError ?? "Press Start to listen") { SidebarText = "Twitch offline" },
        _ => new PluginStatus(PluginStatusLevel.Off, "Not set up", "Log in on the Twitch page") { SidebarText = "Twitch not set up" }
    };

    public Control CreateGuideSection() => PluginManager.ToysEnabled
        ? Ui.Card("Twitch",
            "Let your chat and supporters make you feel it. Everything is set up on the Twitch page.",
            LoginStep(),
            Ui.NumberedStep(2, "Make rules", "Each rule says what has to happen (bits, a sub tier, a redeem, a chat command…) and how strong and long it buzzes. When several rules match, the strongest one wins and the longest one decides the length."),
            Ui.NumberedStep(3, "Test", "Every rule has a Test button, so you can feel it before going live."),
            PrimeInfo(), RedeemInfo())
        : Ui.Card("Twitch",
            "Your channel's bits, subs, redeems, raids and chat commands drive the rules of your other plugins, like VRChat.",
            LoginStep(),
            Ui.NumberedStep(2, "Make rules where they act", "On the VRChat page, each rule says what has to happen on Twitch and what VRChat should do."),
            PrimeInfo(), RedeemInfo());

    private static Control LoginStep() => Ui.NumberedStep(1, "Log in",
        "Press Log in with Twitch and approve Butthurt in your browser. It can read your bits, subs, redeems and chat, and manage the channel point rewards it creates. Your password never touches Butthurt.");

    private static Control PrimeInfo() => Ui.InfoRow("Prime subs", "Twitch reports Prime subs as tier 1, so a tier 1 rule covers Prime too.");

    private static Control RedeemInfo() => Ui.InfoRow("Channel points", "Redeems only work for affiliates and partners. Leave the reward name empty to react to every redeem.");
}
