using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Overlay.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Overlay;

/// <summary>
/// The stream overlay: a local page for an OBS Browser source showing announcements and the queue.
/// Runs while this plugin is on; other plugins send to it through <see cref="OverlayService"/>.
/// </summary>
public class OverlayPlugin : IPlugin
{
    public const string PluginId = PluginManager.OverlayPluginId;

    public string Id => PluginId;
    public string Name => "Stream overlay";
    public PackIconLucideKind Icon => PackIconLucideKind.MonitorPlay;
    public SideBarType Category => SideBarType.Streaming;

    public event EventHandler? StatusChanged;

    public void Initialize()
    {
        OverlayService.StatusChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        OverlayService.Start();
    }

    public Task Start()
    {
        OverlayService.Start();
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        OverlayService.Stop();
        return Task.CompletedTask;
    }

    public IEnumerable<IPageView> CreatePages() => new IPageView[] { new OverlayView() };

    public PluginStatus GetStatus()
    {
        if (!OverlayService.IsRunning)
        {
            return OverlayService.LastError is { } error
                ? new PluginStatus(PluginStatusLevel.Off, "Problem", error) { SidebarText = "Overlay has a problem" }
                : new PluginStatus(PluginStatusLevel.Off, "Stopped", "Press Start to serve the overlay") { SidebarText = "Overlay stopped" };
        }

        var clients = OverlayService.ConnectedClients;
        if (clients == 0)
        {
            return new PluginStatus(PluginStatusLevel.Waiting, "Waiting for OBS", $"Add {OverlayService.Url} as a Browser source")
                { SidebarText = "Overlay waiting for OBS" };
        }

        return new PluginStatus(PluginStatusLevel.Active, "On stream", clients == 1 ? "1 Browser source connected" : $"{clients} Browser sources connected")
            { SidebarText = "Overlay on stream" };
    }

    public PluginAction? HomeAction => new("Test announcement", PackIconLucideKind.Sparkles, async () =>
    {
        if (!await OverlayView.ShowTestAnnouncement()) return;
        ActivityLog.Add("Test announcement on the overlay", LogLevel.Event);
    });

    public Control CreateGuideSection() => Ui.Card("Stream overlay",
        "Show announcements and the queue of running effects on stream, through a Browser source in OBS.",
        Ui.NumberedStep(1, "Add it to OBS", "On the Stream overlay page press Copy, then in OBS add a Browser source with that address, 1920 × 1080. It has a transparent background."),
        Ui.NumberedStep(2, "Write the announcements", "Each VRChat rule has its own message, in the rule's editor on the Twitch or Fansly page, with placeholders like {user}, {bits} and {multiplier}."),
        Ui.NumberedStep(3, "Place it", "Pick where announcements and the queue go and how big they are. Changes show in OBS right away."),
        Ui.InfoRow("Two sources", "Want the queue and the announcements in different scenes? Add ?alerts=0 or ?queue=0 to the address of each Browser source."),
        Ui.InfoRow("Restyle it", "Customize opens the overlay's html, css and js. Read README.txt there first, a wrong change can break the overlay."));
}
