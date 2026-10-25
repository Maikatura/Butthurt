using System.Collections.Generic;

namespace Butthurt.Contracts.Models;

public class EventSettings
{
    public bool Enabled { get; set; } = true;

    // 0.0 - 1.0
    public double Intensity { get; set; } = 0.5;
    public int DurationMs { get; set; } = 500;
}

public class AppSettings
{
    public string IntifaceUrl { get; set; } = "ws://localhost:12345";
    public bool AutoConnect { get; set; }
    public bool AutoStartWatcher { get; set; } = true;
    public double MasterIntensity { get; set; } = 1.0;
    public int PollIntervalMs { get; set; } = 50;
    public string? WowPath { get; set; }
    public List<string> DisabledDevices { get; set; } = new();
    public Dictionary<WowEventType, EventSettings> WowEvents { get; set; } = new();
}
