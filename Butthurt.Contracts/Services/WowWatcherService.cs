using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Butthurt.Contracts.Models;

namespace Butthurt.Contracts.Services;

/// <summary>
/// Watches the top-left corner of the World of Warcraft window. The addon flashes a small square there
/// in a color per event (see <see cref="WowEvents"/>), which we turn into vibrations.
/// </summary>
public static class WowWatcherService
{
    private static readonly HashSet<string> WowProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Wow", "WowT", "WowB", "Wow-64", "WowClassic", "WowClassicT", "WowClassicB"
    };

    // A couple of pixels in so we never sample the window edge.
    private const int SampleOffset = 2;

    private static CancellationTokenSource? _cts;
    private static IntPtr _lastWindow;
    private static bool _lastWindowIsWow;

    public static bool IsRunning => _cts != null;
    public static bool IsWowFocused { get; private set; }
    public static WowEventDefinition? LastEvent { get; private set; }
    public static DateTime? LastEventTime { get; private set; }

    /// <summary>Raised when started/stopped or WoW gains/loses focus. May fire on any thread.</summary>
    public static event EventHandler? StatusChanged;

    /// <summary>Raised for every detected event, before the toys are told to vibrate. May fire on any thread.</summary>
    public static event EventHandler<WowEventDefinition>? EventDetected;

    public static void Start()
    {
        if (IsRunning) return;

        if (!OperatingSystem.IsWindows())
        {
            ActivityLog.Add("The WoW watcher only works on Windows for now", LogLevel.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => WatchLoop(token), token);

        ActivityLog.Add("Watching for World of Warcraft");
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Stop()
    {
        if (!IsRunning) return;

        _cts!.Cancel();
        _cts = null;
        IsWowFocused = false;

        ActivityLog.Add("Stopped watching World of Warcraft");
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Pretend the addon sent an event, used by the test buttons.</summary>
    public static void Simulate(WowEventType type) => Raise(WowEvents.Get(type), force: true);

    private static async Task WatchLoop(CancellationToken token)
    {
        WowEventDefinition? previous = null;
        var lastRaised = DateTime.MinValue;
        var screenDc = GetDC(IntPtr.Zero);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var current = Sample(screenDc);

                // React to the moment the color appears. The addon keeps the pixel lit while hits keep
                // coming, so if it stays lit longer than the vibration we buzz again instead of going quiet.
                if (current != null &&
                    (current != previous ||
                     DateTime.Now - lastRaised >= TimeSpan.FromMilliseconds(SettingsService.GetEvent(current.Type).DurationMs)))
                {
                    Raise(current, force: false);
                    lastRaised = DateTime.Now;
                }

                previous = current;
                await Task.Delay(Math.Clamp(SettingsService.Current.PollIntervalMs, 10, 1000), token);
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception e)
        {
            ActivityLog.Add($"WoW watcher crashed: {e.Message}", LogLevel.Error);
            _cts = null;
            StatusChanged?.Invoke(null, EventArgs.Empty);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static WowEventDefinition? Sample(IntPtr screenDc)
    {
        var hWnd = GetForegroundWindow();
        var focused = IsWowWindow(hWnd);

        if (focused != IsWowFocused)
        {
            IsWowFocused = focused;
            StatusChanged?.Invoke(null, EventArgs.Empty);
        }

        if (!focused) return null;

        // Client area, so the title bar of windowed mode doesn't get in the way.
        var origin = new Point { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref origin)) return null;

        var color = GetPixel(screenDc, origin.X + SampleOffset, origin.Y + SampleOffset);
        if (color == ClrInvalid) return null;

        var r = (byte)(color & 0xFF);
        var g = (byte)((color >> 8) & 0xFF);
        var b = (byte)((color >> 16) & 0xFF);

        return WowEvents.Match(r, g, b);
    }

    private static void Raise(WowEventDefinition definition, bool force)
    {
        var settings = SettingsService.GetEvent(definition.Type);
        if (!settings.Enabled && !force) return;

        LastEvent = definition;
        LastEventTime = DateTime.Now;

        ActivityLog.Add(force ? $"Test: {definition.Name}" : definition.Name, LogLevel.Event);
        EventDetected?.Invoke(null, definition);

        _ = ButthurtService.Vibrate(settings.Intensity, settings.DurationMs);
    }

    private static bool IsWowWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        if (hWnd == _lastWindow) return _lastWindowIsWow;

        _lastWindow = hWnd;
        _lastWindowIsWow = false;

        try
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            using var process = Process.GetProcessById((int)pid);
            _lastWindowIsWow = WowProcessNames.Contains(process.ProcessName);
        }
        catch
        {
            // Process exited or we are not allowed to look at it.
        }

        return _lastWindowIsWow;
    }

    private const uint ClrInvalid = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hDc, int x, int y);
}
