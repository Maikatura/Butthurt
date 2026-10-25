using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buttplug.Client;

namespace Butthurt.Contracts.Services;

/// <summary>
/// Talks to Intiface Central and drives the connected toys.
/// </summary>
public static class ButthurtService
{
    private static ButtplugClient? _client;
    private static bool _exitHooked;

    // Every command to the toys goes through this lock, so a "stop" can never overtake a newer "start".
    private static readonly SemaphoreSlim _toyLock = new(1, 1);
    private static DateTime _vibrateUntil;
    private static double _currentIntensity;
    private static ButtplugClientDevice[] _activeDevices = Array.Empty<ButtplugClientDevice>();
    private static Task? _stopper;

    // Set when the user disconnects on purpose, so we don't try to reconnect.
    private static bool _stopRequested;
    private static DateTime _lastSkipLog;

    public static EventHandler<DeviceAddedEventArgs>? DeviceAdded;
    public static EventHandler<DeviceRemovedEventArgs>? DeviceRemoved;

    /// <summary>Raised when connecting, disconnecting or scanning changes. May fire on any thread.</summary>
    public static event EventHandler? StateChanged;

    public static bool IsConnected => _client?.Connected == true;
    public static bool IsConnecting { get; private set; }
    public static bool IsScanning { get; private set; }
    public static string? LastError { get; private set; }

    public static IReadOnlyList<ButtplugClientDevice> Devices =>
        IsConnected ? _client!.Devices : Array.Empty<ButtplugClientDevice>();

    public static async Task<bool> Start() => await Start(SettingsService.Current.IntifaceUrl);

    public static async Task<bool> Start(string url)
    {
        if (!SettingsService.Current.ToysEnabled) return false;
        if (IsConnected || IsConnecting) return IsConnected;

        if (!_exitHooked)
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _exitHooked = true;
        }

        IsConnecting = true;
        _stopRequested = false;
        LastError = null;
        StateChanged?.Invoke(null, EventArgs.Empty);

        var client = new ButtplugClient("Butthurt - Game plugin manager");
        client.DeviceAdded += (obj, args) =>
        {
            ActivityLog.Add($"Toy connected: {args.Device.Name}", LogLevel.Success);
            DeviceAdded?.Invoke(obj, args);
            StateChanged?.Invoke(null, EventArgs.Empty);
        };
        client.DeviceRemoved += (obj, args) =>
        {
            ActivityLog.Add($"Toy disconnected: {args.Device.Name}", LogLevel.Warning);
            DeviceRemoved?.Invoke(obj, args);
            StateChanged?.Invoke(null, EventArgs.Empty);
        };
        client.ServerDisconnect += (_, _) =>
        {
            StateChanged?.Invoke(null, EventArgs.Empty);
            if (_stopRequested || _client != client) return;

            ActivityLog.Add("Lost connection to Intiface Central, trying to reconnect…", LogLevel.Warning);
            _client = null;
            _ = Reconnect(url);
        };

        try
        {
            await client.ConnectAsync(new ButtplugWebsocketConnector(new Uri(url)));
            _client = client;
            ActivityLog.Add($"Connected to Intiface Central ({url})", LogLevel.Success);
        }
        catch (Exception e)
        {
            var message = e is ButtplugClientConnectorException { InnerException: { } inner } ? inner.Message : e.Message;
            LastError = message.Replace(" More detailed information in inner exception.", string.Empty);
            ActivityLog.Add($"Could not connect to Intiface Central at {url}: {LastError}", LogLevel.Error);
        }
        finally
        {
            IsConnecting = false;
            StateChanged?.Invoke(null, EventArgs.Empty);
        }

        if (IsConnected)
        {
            // Pick up toys that Intiface already knows about plus anything turned on just now.
            _ = ScanForDevices();
        }

        return IsConnected;
    }

    public static async Task<List<string>?> ScanForDevices(int durationMs = 5000)
    {
        if (!IsConnected || IsScanning)
        {
            return null;
        }

        IsScanning = true;
        StateChanged?.Invoke(null, EventArgs.Empty);

        try
        {
            await _client!.StartScanningAsync();
            await Task.Delay(durationMs);
            if (IsConnected) await _client.StopScanningAsync();
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Scanning failed: {e.Message}", LogLevel.Error);
        }
        finally
        {
            IsScanning = false;
            StateChanged?.Invoke(null, EventArgs.Empty);
        }

        return GetDevices();
    }

    public static async Task Stop()
    {
        _stopRequested = true;
        var client = _client;
        if (client == null) return;

        try
        {
            if (client.Connected)
            {
                await StopAllDevices();
                await client.DisconnectAsync();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error while disconnecting: {e.Message}");
        }
        finally
        {
            _client = null;
        }

        ActivityLog.Add("Disconnected from Intiface Central");
        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool IsDeviceEnabled(string deviceName) =>
        !SettingsService.Current.DisabledDevices.Contains(deviceName);

    public static void SetDeviceEnabled(string deviceName, bool enabled)
    {
        var disabled = SettingsService.Current.DisabledDevices;
        disabled.Remove(deviceName);
        if (!enabled) disabled.Add(deviceName);
        SettingsService.Save();
    }

    public static bool CanVibrate(ButtplugClientDevice device) => device.VibrateAttributes.Count > 0;

    /// <summary>
    /// Vibrates every enabled toy. Overlapping calls are merged: the vibration lasts until the latest one ends
    /// and runs at the strongest intensity that is still active, so a death buzz isn't cut short by a small hit.
    /// </summary>
    /// <param name="intensity">0.0 - 1.0, scaled by the master intensity.</param>
    public static Task Vibrate(double intensity, int durationMs) =>
        Vibrate(intensity, durationMs, Devices.Where(d => IsDeviceEnabled(d.Name)));

    public static Task VibrateDevice(ButtplugClientDevice device, double intensity, int durationMs) =>
        Vibrate(intensity, durationMs, new[] { device });

    private static async Task Vibrate(double intensity, int durationMs, IEnumerable<ButtplugClientDevice> targets)
    {
        // Toy features off: stay quiet, no "didn't vibrate" warnings either.
        if (!SettingsService.Current.ToysEnabled) return;

        if (!IsConnected)
        {
            LogSkipped("not connected to Intiface Central");
            return;
        }

        var devices = targets.Where(CanVibrate).ToArray();
        if (devices.Length == 0)
        {
            LogSkipped("no enabled toys that can vibrate");
            return;
        }

        intensity = Math.Clamp(intensity * SettingsService.Current.MasterIntensity, 0, 1);
        if (intensity <= 0) return;

        await _toyLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var active = now < _vibrateUntil;
            var until = now.AddMilliseconds(durationMs);

            _vibrateUntil = active && _vibrateUntil > until ? _vibrateUntil : until;
            _currentIntensity = active ? Math.Max(_currentIntensity, intensity) : intensity;
            _activeDevices = active ? _activeDevices.Union(devices).ToArray() : devices;

            await Send(_activeDevices, _currentIntensity);

            if (_stopper == null || _stopper.IsCompleted)
            {
                _stopper = Task.Run(StopWhenDone);
            }
        }
        finally
        {
            _toyLock.Release();
        }
    }

    /// <summary>Waits until the current vibration runs out (it can be extended meanwhile), then stops the toys.</summary>
    private static async Task StopWhenDone()
    {
        while (true)
        {
            TimeSpan wait;
            await _toyLock.WaitAsync();
            try
            {
                wait = _vibrateUntil - DateTime.UtcNow;
                if (wait <= TimeSpan.Zero)
                {
                    await Send(_activeDevices, 0);
                    _activeDevices = Array.Empty<ButtplugClientDevice>();
                    _currentIntensity = 0;
                    return;
                }
            }
            finally
            {
                _toyLock.Release();
            }

            await Task.Delay(wait);
        }
    }

    private static async Task Send(IEnumerable<ButtplugClientDevice> devices, double intensity)
    {
        await Task.WhenAll(devices.Select(async device =>
        {
            try
            {
                await device.VibrateAsync(intensity <= 0 ? 0 : Math.Max(intensity, LowestStep(device)));
            }
            catch (Exception e)
            {
                ActivityLog.Add($"Vibration failed on {device.Name}: {e.Message}", LogLevel.Error);
            }
        }));
    }

    // Toys only have a few speed steps (Lovense has 20), anything below the first step would round down to off.
    private static double LowestStep(ButtplugClientDevice device)
    {
        var steps = device.VibrateAttributes.Select(a => (int)a.StepCount).DefaultIfEmpty(0).Max();
        return steps > 0 ? 1.0 / steps : 0;
    }

    private static async Task StopAllDevices()
    {
        await _toyLock.WaitAsync();
        try
        {
            _vibrateUntil = DateTime.MinValue;
            await Send(Devices.Where(CanVibrate), 0);
            _activeDevices = Array.Empty<ButtplugClientDevice>();
            _currentIntensity = 0;
        }
        finally
        {
            _toyLock.Release();
        }
    }

    private static async Task Reconnect(string url)
    {
        for (var attempt = 1; !_stopRequested && !IsConnected; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * attempt, 30)));
            if (_stopRequested || IsConnected || IsConnecting) return;
            await Start(url);
        }
    }

    // Explains silent "nothing happened" moments without flooding the log during combat.
    private static void LogSkipped(string reason)
    {
        if (DateTime.UtcNow - _lastSkipLog < TimeSpan.FromSeconds(10)) return;
        _lastSkipLog = DateTime.UtcNow;
        ActivityLog.Add($"Didn't vibrate: {reason}", LogLevel.Warning);
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            // Run cleanup work in the background, don't block Avalonia dispatcher
            Task.Run(async () =>
            {
                try
                {
                    await Stop();
                }
                catch (TaskCanceledException)
                {
                    // Expected during shutdown, no problem
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception during async cleanup: {ex}");
                }
            }).Wait(2000); // wait up to 2s for graceful shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception on exit: {ex}");
        }
    }

    public static List<string> GetDevices() => Devices.Select(x => x.Name).ToList();
}
