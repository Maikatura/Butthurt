using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Plugins;

public enum PluginStatusLevel
{
    Off,
    Waiting,
    Active
}

/// <summary>A quick action shown on the plugin's card on the home page, e.g. "Auto install addon".</summary>
public record PluginAction(string Label, PackIconLucideKind Icon, Func<Task> Run);

/// <summary>What the home page and sidebar show for a plugin.</summary>
public record PluginStatus(PluginStatusLevel Level, string Headline, string Detail)
{
    /// <summary>Short text for the sidebar status panel, e.g. "Watching WoW".</summary>
    public string SidebarText { get; init; } = Headline;
}

/// <summary>
/// Something that turns events into vibrations: a game, a streaming site, ...
/// Each plugin lives in its own project (Butthurt.Warcraft, Butthurt.Twitch) and only talks to the
/// core services (ButthurtService for the toys, SettingsService for its settings, ActivityLog, ...).
/// </summary>
public interface IPlugin
{
    /// <summary>Stable id, also used as the key for the plugin's settings. Never change it.</summary>
    string Id { get; }

    string Name { get; }
    PackIconLucideKind Icon { get; }

    /// <summary>Called once at startup, after settings are loaded.</summary>
    void Initialize();

    /// <summary>The Start button on the home page. Plugins start listening if they can.</summary>
    Task Start();

    /// <summary>The Stop button on the home page.</summary>
    Task Stop();

    /// <summary>Pages for the sidebar. The first one is opened from the plugin's home card.</summary>
    IEnumerable<IPageView> CreatePages();

    PluginStatus GetStatus();

    /// <summary>Raised when <see cref="GetStatus"/> would return something new. May fire on any thread.</summary>
    event EventHandler? StatusChanged;

    PluginAction? HomeAction => null;

    /// <summary>
    /// The plugin only exists to make toys buzz (e.g. World of Warcraft). It disappears completely
    /// while toy features are turned off in Settings.
    /// </summary>
    bool UsesToys => false;

    /// <summary>Extra section for the Guide page, explaining how this plugin works.</summary>
    Control? CreateGuideSection() => null;
}
