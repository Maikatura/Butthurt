using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Butthurt.Contracts.Services;
using Butthurt.Warcraft.Models;

namespace Butthurt.Warcraft.Services;

/// <summary>
/// Watches the top-left corner of the World of Warcraft window. The addon draws a row of small boxes there,
/// each lit in the color of an event (see <see cref="WowEvents"/>), so events that overlap each get their own
/// box. After the boxes comes a marker box and a black one; from where the marker sits and how wide it is we
/// work out how many boxes there are and how big, so those can be changed in game without touching the app.
/// </summary>
public static class WowWatcherService
{
    private static readonly HashSet<string> WowProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Wow", "WowT", "WowB", "Wow-64", "WowClassic", "WowClassicT", "WowClassicB"
    };

    // Old addons draw one bigger square: read it a couple of pixels in so we never sample the window edge.
    private const int LegacySampleOffset = 2;

    // The addon allows up to 8 boxes of 4-12 px. A bit of slack on top so the size can be rounded either way.
    private const int MaxBoxes = 8;
    private const int MinBoxSize = 3;
    private const int MaxBoxSize = 16;

    // Enough of the corner for the largest strip: the boxes, the marker and the black box after it.
    private const int CaptureWidth = (MaxBoxes + 2) * MaxBoxSize;
    private const int CaptureHeight = MaxBoxSize;

    // The row we look for the marker on: inside even the smallest box.
    private const int ScanRow = 2;

    private static CancellationTokenSource? _cts;
    private static IntPtr _lastWindow;
    private static bool _lastWindowIsWow;

    public static bool IsRunning => _cts != null;
    public static bool IsWowFocused { get; private set; }

    /// <summary>How many boxes the addon shows, as last read from the game. 0 until read, 1 for old addons.</summary>
    public static int BoxCount { get; private set; }
    public static WowEventDefinition? LastEvent { get; private set; }
    public static DateTime? LastEventTime { get; private set; }

    /// <summary>Raised when started/stopped or WoW gains/loses focus. May fire on any thread.</summary>
    public static event EventHandler? StatusChanged;

    /// <summary>
    /// Raised once when an event shows up in the game (not again while the addon keeps the pixel lit for the same
    /// event, e.g. ongoing damage), so other plugins like PiShock react once per event. May fire on any thread.
    /// </summary>
    public static event EventHandler<WowEventDefinition>? EventDetected;

    private static readonly Dictionary<string, DateTime> LastFired = new();

    private static List<WowRule> Rules => WarcraftPlugin.Settings.GetRules();

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
        BoxCount = 0;

        ActivityLog.Add("Stopped watching World of Warcraft");
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Plays one toy rule as if its event happened, for its Test button. Nothing else reacts.</summary>
    public static void Test(WowRule rule)
    {
        ActivityLog.Add($"Test: {(rule.Name.Length > 0 ? rule.Name : WowEvents.Get(rule.Event).Name)}", LogLevel.Event);
        _ = ButthurtService.Vibrate(rule.Intensity, rule.DurationMs);
    }

    /// <summary>
    /// True when the condition is off cooldown, and starts the cooldown. Public so PiShock's WoW rules use the
    /// same bookkeeping.
    /// </summary>
    public static bool TryUseCooldown(WowCondition condition)
    {
        lock (LastFired)
        {
            if (condition.CooldownSeconds > 0 && LastFired.TryGetValue(condition.Id, out var last) &&
                DateTime.UtcNow - last < TimeSpan.FromSeconds(condition.CooldownSeconds))
            {
                return false;
            }

            LastFired[condition.Id] = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>How long the toys buzz for an event: the longest of its rules that are on (or a short default).</summary>
    private static int LongestBuzz(WowEventType type)
    {
        var durations = Rules.Where(r => r.Enabled && r.Event == type).Select(r => r.DurationMs).ToList();
        return durations.Count > 0 ? durations.Max() : 500;
    }

    private static async Task WatchLoop(CancellationToken token)
    {
        var previous = new HashSet<WowEventType>();
        var lastRaised = new Dictionary<WowEventType, DateTime>();

        try
        {
            using var capture = new CornerCapture();
            while (!token.IsCancellationRequested)
            {
                var current = Sample(capture);

                // React to the moment a color appears in any box. The addon keeps a box lit while hits keep
                // coming, so while it stays lit we buzz again a little before the vibration runs out
                // (the toy service merges them) instead of leaving a gap. Each event keeps its own clock,
                // so ongoing damage and a boss pull at the same time both get through.
                var now = DateTime.Now;
                foreach (var definition in current)
                {
                    var isNew = !previous.Contains(definition.Type);
                    if (isNew || !lastRaised.TryGetValue(definition.Type, out var last) ||
                        now - last >= TimeSpan.FromMilliseconds(LongestBuzz(definition.Type) * 0.75))
                    {
                        Raise(definition, isNew);
                        lastRaised[definition.Type] = now;
                    }
                }

                previous = current.Select(d => d.Type).ToHashSet();
                await Task.Delay(Math.Clamp(WarcraftPlugin.Settings.PollIntervalMs, 10, 1000), token);
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
    }

    /// <summary>The events lit in the corner right now, each once, in box order.</summary>
    private static List<WowEventDefinition> Sample(CornerCapture capture)
    {
        var found = new List<WowEventDefinition>();

        var hWnd = GetForegroundWindow();
        var focused = IsWowWindow(hWnd);

        if (focused != IsWowFocused)
        {
            IsWowFocused = focused;
            StatusChanged?.Invoke(null, EventArgs.Empty);
        }

        if (!focused) return found;

        // Client area, so the title bar of windowed mode doesn't get in the way.
        var origin = new Point { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref origin)) return found;
        if (!capture.Grab(origin.X, origin.Y)) return found;

        void Add(int x, int y)
        {
            var (r, g, b) = capture.Pixel(x, y);
            if (WowEvents.Match(r, g, b) is { } definition && !found.Contains(definition)) found.Add(definition);
        }

        if (FindBoxes(capture) is { } boxes)
        {
            SetBoxCount(boxes.Count);
            var center = (int)(boxes.Size / 2);
            for (var i = 0; i < boxes.Count; i++) Add((int)(i * boxes.Size + boxes.Size / 2), center);
        }
        else
        {
            // No marker: an addon from before the boxes, with a single square.
            SetBoxCount(1);
            Add(LegacySampleOffset, LegacySampleOffset);
        }

        return found;
    }

    /// <summary>
    /// Finds the marker after the boxes and works out how many boxes there are and how wide each is (in screen
    /// pixels, not rounded so the boxes don't drift when the addon's size is a fraction of a pixel off).
    /// Null when there is no marker, or what looks like one doesn't add up.
    /// </summary>
    private static (int Count, double Size)? FindBoxes(CornerCapture capture)
    {
        var x = 0;
        while (x < CaptureWidth && !IsMarker(x)) x++;
        var start = x;
        while (x < CaptureWidth && IsMarker(x)) x++;
        var width = x - start;

        if (start == 0 || width < MinBoxSize || width > MaxBoxSize) return null;

        // The marker can come out a pixel wider or narrower than the boxes when the game rounds, so take the
        // count that lines up best with where the marker starts, preferring the width we measured.
        var count = 0;
        var bestError = double.MaxValue;
        foreach (var candidate in new[] { width, width - 1, width + 1 })
        {
            if (candidate < MinBoxSize) continue;
            var c = (int)Math.Round(start / (double)candidate);
            var error = Math.Abs(start - c * candidate) / (double)candidate;
            if (c >= 1 && c <= MaxBoxes && error < bestError - 0.001)
            {
                count = c;
                bestError = error;
            }
        }

        if (count == 0 || bestError > 0.25) return null;

        // The addon puts a black box right after the marker, so a stray purple bit of the game world can't pass.
        var size = start / (double)count;
        var guard = (int)(start + size * 1.5);
        if (guard >= CaptureWidth) return null;
        var (r, g, b) = capture.Pixel(guard, ScanRow);
        if (!WowEvents.IsIdle(r, g, b)) return null;

        return (count, size);

        bool IsMarker(int px)
        {
            var (pr, pg, pb) = capture.Pixel(px, ScanRow);
            return WowEvents.IsMarker(pr, pg, pb);
        }
    }

    private static void SetBoxCount(int count)
    {
        if (count == BoxCount) return;
        BoxCount = count;
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void Raise(WowEventDefinition definition, bool isNew)
    {
        if (isNew)
        {
            LastEvent = definition;
            LastEventTime = DateTime.Now;
            ActivityLog.Add(definition.Name, LogLevel.Event);

            // Other plugins (PiShock) once per event, with their own rules and cooldowns.
            EventDetected?.Invoke(null, definition);
        }

        // Toy rules: every rule for this event that's on. While the pixel stays lit (ongoing damage) they keep
        // buzzing; cooldowns only count fresh events, so a long fight doesn't get cut into pieces.
        foreach (var rule in Rules.Where(r => r.Enabled && r.Event == definition.Type).ToList())
        {
            if (isNew && !TryUseCooldown(rule)) continue;
            if (!isNew && rule.CooldownSeconds > 0) continue;
            _ = ButthurtService.Vibrate(rule.Intensity, rule.DurationMs);
        }
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

    /// <summary>
    /// Copies the top-left corner of the screen into memory in one go, instead of asking Windows for every
    /// pixel on its own (which is slow, and there can be a dozen boxes' worth now).
    /// </summary>
    private sealed class CornerCapture : IDisposable
    {
        private readonly IntPtr _screenDc;
        private readonly IntPtr _memoryDc;
        private readonly IntPtr _bitmap;
        private readonly IntPtr _oldBitmap;
        private readonly IntPtr _bits;
        private readonly byte[] _pixels = new byte[CaptureWidth * CaptureHeight * 4];

        public CornerCapture()
        {
            _screenDc = GetDC(IntPtr.Zero);
            _memoryDc = CreateCompatibleDC(_screenDc);

            var info = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = CaptureWidth,
                Height = -CaptureHeight, // Top-down, so row 0 is the top of the screen.
                Planes = 1,
                BitCount = 32,
                Compression = 0 // BI_RGB
            };
            _bitmap = CreateDIBSection(_memoryDc, ref info, 0, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("Could not create a bitmap to read the game corner into.");
            _oldBitmap = SelectObject(_memoryDc, _bitmap);
        }

        /// <summary>Copies the corner starting at the given screen position. False when Windows refused.</summary>
        public bool Grab(int screenX, int screenY)
        {
            if (!BitBlt(_memoryDc, 0, 0, CaptureWidth, CaptureHeight, _screenDc, screenX, screenY, SrcCopy)) return false;
            GdiFlush();
            Marshal.Copy(_bits, _pixels, 0, _pixels.Length);
            return true;
        }

        public (byte R, byte G, byte B) Pixel(int x, int y)
        {
            x = Math.Clamp(x, 0, CaptureWidth - 1);
            y = Math.Clamp(y, 0, CaptureHeight - 1);
            var i = (y * CaptureWidth + x) * 4;
            return (_pixels[i + 2], _pixels[i + 1], _pixels[i]); // Stored as BGRA.
        }

        public void Dispose()
        {
            SelectObject(_memoryDc, _oldBitmap);
            DeleteObject(_bitmap);
            DeleteDC(_memoryDc);
            ReleaseDC(IntPtr.Zero, _screenDc);
        }
    }

    private const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

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
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hDc, ref BitmapInfoHeader info, uint usage, out IntPtr bits,
        IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX,
        int sourceY, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);
}
