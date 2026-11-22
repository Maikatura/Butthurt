using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Butthurt.Contracts.Models;

/// <summary>Settings of the core app. Plugins keep theirs under <see cref="Plugins"/>, see SettingsService.GetPluginSettings.</summary>
public class AppSettings
{
    /// <summary>
    /// The old "Toy features" switch from before Toys was a plugin. Only read once at startup: if it was off,
    /// the Toys plugin gets turned off instead and this goes back to true. Use PluginManager.ToysEnabled.
    /// </summary>
    public bool ToysEnabled { get; set; } = true;

    public string IntifaceUrl { get; set; } = "ws://localhost:12345";
    public bool AutoConnect { get; set; }
    public double MasterIntensity { get; set; } = 1.0;
    public List<string> DisabledDevices { get; set; } = new();

    /// <summary>Ids of plugins the user turned off. They aren't initialized and don't show up anywhere.</summary>
    public List<string> DisabledPlugins { get; set; } = new();

    public OverlaySettings Overlay { get; set; } = new();

    /// <summary>One section per plugin, keyed by the plugin's id.</summary>
    public Dictionary<string, JsonElement> Plugins { get; set; } = new();

    /// <summary>
    /// Anything we don't recognise, e.g. the WoW settings from before plugins had their own section.
    /// Plugins read their old values from here once.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

/// <summary>The stream overlay: a local web page for an OBS Browser source.</summary>
public class OverlaySettings
{
    /// <summary>
    /// The old on/off switch from before the overlay was a plugin. Only read once at startup (see
    /// <see cref="MovedToPlugin"/>): if it was off, the Stream overlay plugin gets turned off instead.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Whether <see cref="Enabled"/> was already turned into the plugin's on/off state.</summary>
    public bool MovedToPlugin { get; set; }

    public int Port { get; set; } = 7878;
    public bool ShowQueue { get; set; } = true;
    public bool ShowAlerts { get; set; } = true;
    public int AlertSeconds { get; set; } = 6;

    public OverlayPosition AlertPosition { get; set; } = OverlayPosition.TopCenter;
    public OverlayPosition QueuePosition { get; set; } = OverlayPosition.TopLeft;

    /// <summary>Makes everything on the overlay bigger or smaller (1 = normal).</summary>
    public double Scale { get; set; } = 1;
}

public enum OverlayPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}
