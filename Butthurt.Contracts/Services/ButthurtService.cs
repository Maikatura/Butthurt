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
    private static CancellationTokenSource? _vibrationCts;
    private static readonly object _vibrationLock = new();
    private static bool _exitHooked;

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
        if (IsConnected || IsConnecting) return IsConnected;

        if (!_exitHooked)
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _exitHooked = true;
        }

        IsConnecting = true;
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
            ActivityLog.Add("Lost connection to Intiface Central", LogLevel.Warning);
            StateChanged?.Invoke(null, EventArgs.Empty);
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
        var client = _client;
        if (client == null) return;

        _client = null;
        try
        {
            if (client.Connected)
            {
                await StopAllDevices(client);
                await client.DisconnectAsync();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error while disconnecting: {e.Message}");
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
    /// Vibrates every enabled toy. A newer call replaces a running one instead of stacking.
    /// </summary>
    /// <param name="intensity">0.0 - 1.0, scaled by the master intensity.</param>
    public static Task Vibrate(double intensity, int durationMs) =>
        Vibrate(intensity, durationMs, Devices.Where(d => IsDeviceEnabled(d.Name)));

    public static Task VibrateDevice(ButtplugClientDevice device, double intensity, int durationMs) =>
        Vibrate(intensity, durationMs, new[] { device });

    private static async Task Vibrate(double intensity, int durationMs, IEnumerable<ButtplugClientDevice> targets)
    {
        if (!IsConnected) return;

        var devices = targets.Where(CanVibrate).ToArray();
        if (devices.Length == 0) return;

        intensity = Math.Clamp(intensity * SettingsService.Current.MasterIntensity, 0, 1);

        CancellationTokenSource cts;
        lock (_vibrationLock)
        {
            _vibrationCts?.Cancel();
            cts = _vibrationCts = new CancellationTokenSource();
        }

        try
        {
            await Task.WhenAll(devices.Select(d => d.VibrateAsync(intensity)));
            await Task.Delay(durationMs, cts.Token);
            await Task.WhenAll(devices.Select(d => d.VibrateAsync(0)));
        }
        catch (TaskCanceledException)
        {
            // A newer vibration took over, it is responsible for stopping the toys.
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Vibration failed: {e.Message}", LogLevel.Error);
        }
    }

    private static async Task StopAllDevices(ButtplugClient client)
    {
        foreach (var device in client.Devices.Where(CanVibrate))
        {
            await device.VibrateAsync(0);
        }
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
