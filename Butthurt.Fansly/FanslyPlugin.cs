using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Fansly.Models;
using Butthurt.Fansly.Services;
using Butthurt.Fansly.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Fansly;

public class FanslyPlugin : IPlugin
{
    public const string PluginId = "Fansly";

    public static FanslySettings Settings
    {
        get
        {
            var settings = SettingsService.GetPluginSettings<FanslySettings>(PluginId);
            settings.Rules ??= FanslyTriggers.DefaultRules();
            return settings;
        }
    }

    private FanslyEvent? _lastEvent;
    private DateTime _lastEventTime;

    public string Id => PluginId;
    public string Name => "Fansly";
    public PackIconLucideKind Icon => PackIconLucideKind.Radio;

    public event EventHandler? StatusChanged;

    public void Initialize()
    {
        FanslyService.StateChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        FanslyService.EventReceived += (_, e) =>
        {
            if (e.Type == FanslyTrigger.ChatCommand) return;
            _lastEvent = e;
            _lastEventTime = DateTime.Now;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        };

        if (Settings.AutoConnect && FanslyService.IsSetUp) _ = FanslyService.Connect();
    }

    public Task Start() => FanslyService.IsSetUp ? FanslyService.Connect() : Task.CompletedTask;

    public Task Stop() => FanslyService.Disconnect();

    public IEnumerable<IPageView> CreatePages() => new IPageView[] { new FanslyView() };

    public PluginStatus GetStatus() => FanslyService.State switch
    {
        FanslyState.Connected => new PluginStatus(PluginStatusLevel.Active, "Live",
            _lastEvent != null
                ? $"Last: {FanslyRules.Describe(_lastEvent)} at {_lastEventTime:HH:mm}"
                : $"Listening to {FanslyService.ChannelName}'s chat") { SidebarText = "Fansly connected" },
        FanslyState.Connecting => new PluginStatus(PluginStatusLevel.Waiting, "Connecting", "Talking to Fansly…") { SidebarText = "Fansly connecting…" },
        _ when FanslyService.IsSetUp => new PluginStatus(PluginStatusLevel.Off, "Offline",
            FanslyService.LastError ?? "Press Start to listen") { SidebarText = "Fansly offline" },
        _ => new PluginStatus(PluginStatusLevel.Off, "Not set up", "Add your username on the Fansly page") { SidebarText = "Fansly not set up" }
    };

    public Control CreateGuideSection() => Ui.Card("Fansly",
        "Tips, subscriptions and chat commands from your Fansly livestream drive vibration rules here and PiShock rules on the PiShock page.",
        Ui.NumberedStep(1, "Add your username", "Type your Fansly username on the Fansly page and press Save. Butthurt finds your livestream chat by itself, no password needed."),
        Ui.NumberedStep(2, "Make rules", "Each rule says what has to happen (a tip in a dollar range, a sub tier, a chat command) and how strong and long it buzzes."),
        Ui.NumberedStep(3, "Go live", "Butthurt reads the chat while you stream. Every rule has a Test button to try it first."),
        Ui.InfoRow("Unofficial", "Fansly has no public API for this, Butthurt reads the chat the way the website does. If Fansly changes something it may stop working until Butthurt is updated."),
        Ui.InfoRow("Fansly asks for a login?", "Normally the chat is read anonymously. If the activity log says Fansly wants a login, paste your session token on the Fansly page."));
}
