using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Warcraft.Models;
using Butthurt.Warcraft.Services;
using Butthurt.Warcraft.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Warcraft;

public class WarcraftPlugin : IPlugin
{
    public const string PluginId = "Warcraft";

    public static WarcraftSettings Settings => SettingsService.GetPluginSettings<WarcraftSettings>(PluginId);

    public string Id => PluginId;
    public string Name => "World of Warcraft";
    public PackIconLucideKind Icon => PackIconLucideKind.Swords;

    public event EventHandler? StatusChanged;

    public void Initialize()
    {
        WowWatcherService.StatusChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        WowWatcherService.EventDetected += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);

        if (Settings.AutoStartWatcher) WowWatcherService.Start();
    }

    public Task Start()
    {
        WowWatcherService.Start();
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        WowWatcherService.Stop();
        return Task.CompletedTask;
    }

    public IEnumerable<IPageView> CreatePages() => new IPageView[] { new WowView() };

    public PluginStatus GetStatus()
    {
        if (!WowWatcherService.IsRunning)
        {
            return new PluginStatus(PluginStatusLevel.Off, "Stopped", "The watcher is not running") { SidebarText = "WoW watcher stopped" };
        }

        if (!WowWatcherService.IsWowFocused)
        {
            return new PluginStatus(PluginStatusLevel.Waiting, "Waiting", "Switch to the game window") { SidebarText = "Waiting for WoW" };
        }

        var detail = WowWatcherService.LastEvent is { } last && WowWatcherService.LastEventTime is { } time
            ? $"Last: {last.Name} at {time:HH:mm:ss}"
            : "Ready for events";
        return new PluginStatus(PluginStatusLevel.Active, "Watching", detail) { SidebarText = "Watching WoW" };
    }

    // Its events only make toys buzz, so it goes away with the toy features.
    public bool UsesToys => true;

    public PluginAction HomeAction => new("Auto install addon", PackIconLucideKind.Download, AddonInstaller.AutoInstallAndNotify);

    public Control CreateGuideSection() => GuideSection.Create();
}
