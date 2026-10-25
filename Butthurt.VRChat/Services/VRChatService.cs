using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Butthurt.Contracts.Services;
using Butthurt.VRChat.Models;

namespace Butthurt.VRChat.Services;

/// <summary>An avatar swap from a rule: which avatar and for how long (0 = keep it).</summary>
public record AvatarSwap(VRChatRule Rule, string AvatarId, int Seconds);

/// <summary>A running "grow" from one rule. Every active effect's multiplier is multiplied together.</summary>
public record GrowEffect(string RuleId, string Name, double Multiplier, DateTime ExpiresUtc);

/// <summary>
/// A running parameter change from one rule. Keeps a reference to the rule, so its per-avatar parameter
/// names are looked up again after every avatar change.
/// </summary>
public record ParameterEffect(VRChatRule Rule, DateTime StartedUtc, DateTime ExpiresUtc);

/// <summary>
/// Talks to VRChat over OSC: switches avatars (/avatar/change) and sets your eye height (/avatar/eyeheight).
/// Grow effects stack by multiplying, run out on their own and put you back at your normal height.
/// </summary>
public static class VRChatService
{
    private const string AvatarChangeAddress = "/avatar/change";
    private const string EyeHeightAddress = "/avatar/eyeheight";
    private const string ParameterPrefix = "/avatar/parameters/";

    // A new avatar resets its parameters and size once it has loaded, which can take a while, so
    // everything active is sent again a few times after a switch.
    private static readonly TimeSpan[] ReapplyDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

    // VRChat only officially supports eye heights between these.
    private const double SupportedMinHeight = 0.1;
    private const double SupportedMaxHeight = 100;

    private static readonly object _lock = new();
    private static readonly List<GrowEffect> _effects = new();
    private static readonly List<ParameterEffect> _parameters = new();
    private static readonly List<DateTime> _reapplyAt = new();
    private static readonly HashSet<string> _seenParameters = new();

    // What we last sent per parameter name, so a parameter whose effects all ended can be put back.
    private static Dictionary<string, (ParameterType Type, double Reset)> _sentParameters = new();
    private static DateTime _avatarChangedUtc;

    private static UdpClient? _sender;
    private static UdpClient? _listener;
    private static CancellationTokenSource? _cts;

    // Temporary avatar swaps take turns: one is worn until its time is up, the rest wait in line, and
    // when the line is empty you go back to the avatar you had before the first swap (the "home" avatar).
    private static readonly List<AvatarSwap> _avatarQueue = new();
    private static AvatarSwap? _currentSwap;
    private static DateTime _currentSwapEndsUtc;
    private static string? _homeAvatarId;

    /// <summary>The temporary avatar being worn right now and when it ends, or null.</summary>
    public static (AvatarSwap Swap, DateTime EndsUtc)? CurrentSwap
    {
        get
        {
            lock (_lock) return _currentSwap is { } swap ? (swap, _currentSwapEndsUtc) : null;
        }
    }

    /// <summary>Swaps waiting for their turn, first in line first.</summary>
    public static IReadOnlyList<AvatarSwap> AvatarQueue
    {
        get
        {
            lock (_lock) return _avatarQueue.ToList();
        }
    }

    /// <summary>What you go back to when the line of swaps is done.</summary>
    public static string? HomeAvatarId => _homeAvatarId;

    /// <summary>Seconds until the avatar swap that was just accepted starts (0 = right away). Used by announcements.</summary>
    public static int LastSwapWaitSeconds { get; private set; }
    private static DateTime _lastHeightWriteUtc;
    private static DateTime _vrchatCheckedUtc;
    private static bool _vrchatRunning;

    public static bool IsRunning => _cts != null;

    /// <summary>False when another program already listens on VRChat's reply port.</summary>
    public static bool IsListening => _listener != null;

    public static DateTime? LastReceivedUtc { get; private set; }

    /// <summary>VRChat sent us something recently, so it is definitely listening too.</summary>
    public static bool IsConnected => LastReceivedUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromSeconds(20);

    public static string? CurrentAvatarId { get; private set; }
    public static double CurrentEyeHeight { get; private set; }

    /// <summary>Raised when the connection, avatar, size or effects change. May fire on any thread.</summary>
    public static event EventHandler? StatusChanged;

    private static VRChatSettings Settings => VRChatPlugin.Settings;

    /// <summary>Whether the VRChat game is open at all. Checked at most every couple of seconds.</summary>
    public static bool IsVRChatRunning
    {
        get
        {
            if (DateTime.UtcNow - _vrchatCheckedUtc > TimeSpan.FromSeconds(2))
            {
                var processes = Process.GetProcessesByName("VRChat");
                _vrchatRunning = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                _vrchatCheckedUtc = DateTime.UtcNow;
            }

            return _vrchatRunning;
        }
    }

    public static IReadOnlyList<ParameterEffect> ActiveParameters
    {
        get
        {
            lock (_lock) return _parameters.ToList();
        }
    }

    /// <summary>Parameter names VRChat reported for the avatar you're wearing, as suggestions for rules.</summary>
    public static IReadOnlyList<string> SeenParameters
    {
        get
        {
            lock (_lock) return _seenParameters.OrderBy(n => n).ToList();
        }
    }

    public static IReadOnlyList<GrowEffect> ActiveEffects
    {
        get
        {
            lock (_lock) return _effects.ToList();
        }
    }

    /// <summary>All active multipliers multiplied together, kept inside the min/max from the settings.</summary>
    public static double CurrentMultiplier
    {
        get
        {
            lock (_lock) return Multiplier(_effects);
        }
    }

    public static void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _sender = new UdpClient();

        try
        {
            _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, Settings.ListenPort));
            _ = Task.Run(() => ListenLoop(_listener, _cts.Token));
        }
        catch (SocketException)
        {
            _listener = null;
            ActivityLog.Add($"Port {Settings.ListenPort} is used by another OSC app, so Butthurt can't see your current avatar or height. Sending still works.", LogLevel.Warning);
        }

        _ = Task.Run(() => TickLoop(_cts.Token));
        CurrentEyeHeight = Settings.BaseEyeHeight;
        ActivityLog.Add($"Talking to VRChat over OSC ({Settings.OscHost}:{Settings.SendPort})");
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Stop()
    {
        if (!IsRunning) return;

        lock (_lock) _effects.Clear();
        SendHeight(instant: true);
        ResetParameters();
        ResetAvatars();
        _cts!.Cancel();
        _cts = null;
        _listener?.Dispose();
        _listener = null;
        _sender?.Dispose();
        _sender = null;

        ActivityLog.Add("Stopped talking to VRChat");
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Runs a rule's action. Returns false when it couldn't (VRChat closed, no avatar set…).</summary>
    public static bool Apply(VRChatRule rule)
    {
        if (!IsRunning)
        {
            ActivityLog.Add($"VRChat: skipped {rule.Name}, the VRChat plugin isn't started", LogLevel.Warning);
            return false;
        }

        if (!IsVRChatRunning)
        {
            ActivityLog.Add($"VRChat: skipped {rule.Name}, VRChat isn't running", LogLevel.Warning);
            return false;
        }

        return rule.Action switch
        {
            VRChatAction.ChangeAvatar => ChangeAvatar(rule),
            VRChatAction.Grow => Grow(rule),
            VRChatAction.SetParameter => SetParameter(rule),
            _ => false
        };
    }

    /// <summary>Puts every parameter Butthurt changed back to its rule's reset value.</summary>
    public static void ResetParameters()
    {
        lock (_lock) _parameters.Clear();
        SendParameters();
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool SetParameter(VRChatRule rule)
    {
        var name = rule.ParameterNameFor(CurrentAvatarId);
        if (name == null)
        {
            ActivityLog.Add($"VRChat: {rule.Name} has no parameter name for the avatar you're wearing", LogLevel.Warning);
            return false;
        }

        var now = DateTime.UtcNow;
        var expires = rule.DurationSeconds > 0 ? now.AddSeconds(rule.DurationSeconds) : DateTime.MaxValue;
        lock (_lock)
        {
            // Setting again only restarts the timer, adding again stacks.
            if (rule.ParameterMode == ParameterMode.Set || rule.ParameterType == ParameterType.Bool)
            {
                _parameters.RemoveAll(p => p.Rule.Id == rule.Id);
            }

            _parameters.Add(new ParameterEffect(rule, now, expires));
        }

        var sent = SendParameters();
        ActivityLog.Add($"VRChat: {rule.Name} → {name}" + (rule.DurationSeconds > 0 ? $" for {rule.DurationSeconds}s" : ""), LogLevel.Success);
        StatusChanged?.Invoke(null, EventArgs.Empty);
        return sent;
    }

    /// <summary>
    /// The value every parameter should have on this avatar: the newest Set wins (or the reset value when
    /// there is none) and every Add is added on top. Public for the tests.
    /// </summary>
    public static Dictionary<string, object> ParameterValues(IEnumerable<ParameterEffect> effects, string? avatarId)
    {
        var values = new Dictionary<string, object>();

        var byName = effects
            .Select(e => (Effect: e, Name: e.Rule.ParameterNameFor(avatarId)))
            .Where(x => x.Name != null)
            .GroupBy(x => x.Name!, StringComparer.Ordinal);

        foreach (var group in byName)
        {
            var ordered = group.Select(x => x.Effect).OrderBy(e => e.StartedUtc).ToList();
            var newest = ordered[^1].Rule;
            var newestSet = ordered.LastOrDefault(e => e.Rule.ParameterMode == ParameterMode.Set || e.Rule.ParameterType == ParameterType.Bool);

            var value = (newestSet?.Rule.ParameterValue ?? newest.ParameterResetValue) +
                        ordered.Where(e => e.Rule.ParameterMode == ParameterMode.Add && e.Rule.ParameterType != ParameterType.Bool)
                            .Sum(e => e.Rule.ParameterValue);

            values[group.Key] = Typed(newest.ParameterType, value);
        }

        return values;
    }

    // VRChat's ranges: ints are 0-255, floats -1 to 1.
    private static object Typed(ParameterType type, double value) => type switch
    {
        ParameterType.Bool => value >= 0.5,
        ParameterType.Int => (int)Math.Clamp(Math.Round(value), 0, 255),
        _ => (float)Math.Clamp(value, -1, 1)
    };

    /// <summary>Sends every active parameter for the current avatar, and resets the ones that just ended.</summary>
    private static bool SendParameters()
    {
        List<ParameterEffect> active;
        lock (_lock) active = _parameters.ToList();

        var values = ParameterValues(active, CurrentAvatarId);
        var ok = true;

        foreach (var (name, value) in values)
        {
            ok &= Send(ParameterPrefix + name, value);
        }

        // Parameters nothing drives anymore go back to the reset value of the rule that last drove them.
        var sentBefore = _sentParameters;
        foreach (var (name, (type, reset)) in sentBefore)
        {
            if (!values.ContainsKey(name)) Send(ParameterPrefix + name, Typed(type, reset));
        }

        _sentParameters = active
            .Select(e => (Name: e.Rule.ParameterNameFor(CurrentAvatarId), e.Rule))
            .Where(x => x.Name != null)
            .GroupBy(x => x.Name!)
            .ToDictionary(g => g.Key, g => (g.Last().Rule.ParameterType, g.Last().Rule.ParameterResetValue));

        return ok;
    }

    /// <summary>A new avatar starts fresh: send what's active again once it has loaded.</summary>
    private static void OnAvatarChanged()
    {
        _avatarChangedUtc = DateTime.UtcNow;
        lock (_lock)
        {
            // Stop any slide right away, so its steps don't hide the height the new avatar loads at.
            _snap = true;
            _velocity = 0;

            _seenParameters.Clear();
            _reapplyAt.Clear();
            _reapplyAt.AddRange(ReapplyDelays.Select(d => DateTime.UtcNow + d));
        }

        // The old avatar's parameter names mean nothing on the new one, don't "reset" them there.
        _sentParameters = new Dictionary<string, (ParameterType, double)>();
    }

    private static void Reapply()
    {
        bool growing, hasParameters;
        lock (_lock)
        {
            growing = _effects.Count > 0;
            hasParameters = _parameters.Count > 0;
        }

        if (growing) SendHeight();
        if (hasParameters) SendParameters();
    }

    /// <summary>Back to your normal height, every grow effect removed.</summary>
    public static void ResetSize()
    {
        lock (_lock) _effects.Clear();
        SendHeight();
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool ChangeAvatar(VRChatRule rule)
    {
        var avatarId = rule.AvatarId.Trim();
        if (!avatarId.StartsWith("avtr_", StringComparison.OrdinalIgnoreCase))
        {
            ActivityLog.Add($"VRChat: {rule.Name} has no avatar ID (it starts with avtr_)", LogLevel.Warning);
            return false;
        }

        var swap = new AvatarSwap(rule, avatarId, Math.Max(0, rule.RevertAfterSeconds));

        lock (_lock)
        {
            // Someone else's swap is still on: wait in line behind it (and anyone already waiting).
            if (_currentSwap != null)
            {
                LastSwapWaitSeconds = (int)Math.Ceiling(Math.Max(0, (_currentSwapEndsUtc - DateTime.UtcNow).TotalSeconds) +
                                                        _avatarQueue.Sum(q => q.Seconds));
                _avatarQueue.Add(swap);
                ActivityLog.Add($"VRChat: {rule.Name} is waiting its turn (starts in about {LastSwapWaitSeconds}s)", LogLevel.Success);
                StatusChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
        }

        LastSwapWaitSeconds = 0;
        return StartSwap(swap);
    }

    /// <summary>Puts a swap on. Temporary ones remember the home avatar to come back to.</summary>
    private static bool StartSwap(AvatarSwap swap)
    {
        // Home is the avatar from before the first swap in a row, never one of the swaps.
        var home = _homeAvatarId ?? CurrentAvatarId ?? NullIfEmpty(Settings.DefaultAvatarId);

        if (!Send(AvatarChangeAddress, swap.AvatarId)) return false;
        CurrentAvatarId = swap.AvatarId;
        OnAvatarChanged();

        lock (_lock)
        {
            if (swap.Seconds > 0)
            {
                _currentSwap = swap;
                _currentSwapEndsUtc = DateTime.UtcNow.AddSeconds(swap.Seconds);
                _homeAvatarId = home;
            }
            else
            {
                // A swap that keeps the avatar becomes the new home.
                _currentSwap = null;
                _homeAvatarId = null;
            }
        }

        ActivityLog.Add($"VRChat: switched to {AvatarLibrary.DisplayName(swap.AvatarId)} ({swap.Rule.Name})" +
                        (swap.Seconds > 0 ? $" for {swap.Seconds}s" : ""), LogLevel.Success);
        StatusChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    /// <summary>When the current swap's time is up: the next one in line, or back home when nobody's waiting.</summary>
    private static void AdvanceAvatarQueue()
    {
        AvatarSwap? next = null;
        lock (_lock)
        {
            if (_currentSwap == null || _currentSwapEndsUtc > DateTime.UtcNow) return;

            _currentSwap = null;
            if (_avatarQueue.Count > 0)
            {
                next = _avatarQueue[0];
                _avatarQueue.RemoveAt(0);
            }
        }

        if (next != null)
        {
            // A permanent swap waiting in line becomes the new home, then the line carries on.
            if (next.Seconds <= 0)
            {
                StartSwap(next);
                lock (_lock)
                {
                    if (_avatarQueue.Count > 0)
                    {
                        _currentSwap = next with { Seconds = 0 };
                        _currentSwapEndsUtc = DateTime.UtcNow;
                    }
                }
            }
            else
            {
                StartSwap(next);
            }

            return;
        }

        GoHome();
    }

    private static void GoHome()
    {
        var home = _homeAvatarId;
        _homeAvatarId = null;

        if (home != null && home != CurrentAvatarId && Send(AvatarChangeAddress, home))
        {
            CurrentAvatarId = home;
            OnAvatarChanged();
            ActivityLog.Add($"VRChat: switched back to {AvatarLibrary.DisplayName(home)}");
        }

        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Clears the line of swaps and goes back to the home avatar right away.</summary>
    public static void ResetAvatars()
    {
        bool hadSwap;
        lock (_lock)
        {
            hadSwap = _currentSwap != null;
            _avatarQueue.Clear();
            _currentSwap = null;
        }

        if (hadSwap) GoHome();
        else _homeAvatarId = null;
    }

    private static bool Grow(VRChatRule rule)
    {
        lock (_lock)
        {
            // Not stacking: the rule's previous effect is replaced, which restarts its timer.
            if (!rule.Stack) _effects.RemoveAll(e => e.RuleId == rule.Id);
            _effects.Add(new GrowEffect(rule.Id, rule.Name, rule.Multiplier, DateTime.UtcNow.AddSeconds(rule.DurationSeconds)));
        }

        var sent = SendHeight();
        ActivityLog.Add($"VRChat: {rule.Name} → ×{CurrentMultiplier:0.##} for {rule.DurationSeconds}s", LogLevel.Success);
        StatusChanged?.Invoke(null, EventArgs.Empty);
        return sent;
    }

    /// <summary>The effects' multiplier, also used by the tests.</summary>
    public static double Multiplier(IEnumerable<GrowEffect> effects)
    {
        var total = effects.Aggregate(1.0, (product, effect) => product * effect.Multiplier);
        return Math.Clamp(total, Settings.MinMultiplier, Math.Max(Settings.MinMultiplier, Settings.MaxMultiplier));
    }

    // Smooth size changes. The height follows its target like a critically damped spring: it speeds up,
    // then settles exactly on the target without overshooting. A new target mid-slide keeps the current
    // speed and just bends towards the new value, so there is no stop-and-go stutter.
    private static readonly TimeSpan TransitionStep = TimeSpan.FromMilliseconds(25);
    private static double _transitionTo;
    private static double _velocity;

    // Two springs in a row: a "guide" follows the target, the height follows the guide. That makes the
    // speed itself change smoothly too, at the start and when a new target arrives (no jolt).
    private static double _guide;
    private static double _guideVelocity;
    private static bool _snap;
    private static Task? _transitionTask;

    /// <summary>True while the height is still sliding towards its target.</summary>
    public static bool IsTransitioning => _transitionTask is { IsCompleted: false };

    /// <summary>Where the height is heading (equals CurrentEyeHeight when nothing is sliding).</summary>
    public static double TargetEyeHeight =>
        Math.Clamp(Settings.BaseEyeHeight * CurrentMultiplier, SupportedMinHeight, SupportedMaxHeight);

    /// <summary>
    /// One step of a critically damped spring (the "SmoothDamp" from Game Programming Gems 4).
    /// smoothTime is roughly how long it takes to cover most of the way. Public for the tests.
    /// </summary>
    public static double SpringStep(double current, double target, ref double velocity, double smoothTime, double deltaSeconds)
    {
        smoothTime = Math.Max(0.0001, smoothTime);
        var omega = 2.0 / smoothTime;
        var x = omega * deltaSeconds;
        var decay = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);

        var change = current - target;
        var temp = (velocity + omega * change) * deltaSeconds;
        velocity = (velocity - omega * temp) * decay;
        var next = target + (change + temp) * decay;

        // Never shoot past the target.
        if (target - current > 0 == next > target)
        {
            next = target;
            velocity = 0;
        }

        return next;
    }

    /// <summary>The spring's smooth time for the "Smooth size changes" setting (it arrives within about that long).</summary>
    public static double SmoothTimeFor(double smoothSeconds) => smoothSeconds / 6.0;

    /// <summary>
    /// One step of the two-spring smoothing. Returns the new height. Public for the tests.
    /// </summary>
    public static double SmoothStep(double height, double target, ref double velocity, ref double guide,
        ref double guideVelocity, double smoothTime, double deltaSeconds)
    {
        guide = SpringStep(guide, target, ref guideVelocity, smoothTime, deltaSeconds);
        return SpringStep(height, guide, ref velocity, smoothTime, deltaSeconds);
    }

    /// <summary>True when the height has (practically) arrived and stopped, so it can land exactly on the target.</summary>
    public static bool HasSettled(double height, double target, double velocity, double guide) =>
        Math.Abs(height - target) < 0.0015 && Math.Abs(guide - target) < 0.0015 && Math.Abs(velocity) < 0.02;

    /// <summary>
    /// Sends the height that the active effects ask for. With smoothing on, it slides there from wherever
    /// you are right now, keeping its speed when a new effect lands halfway through a slide.
    /// </summary>
    private static bool SendHeight(bool instant = false)
    {
        var target = TargetEyeHeight;

        if (instant || Settings.SmoothSeconds <= 0 || !IsRunning ||
            (!IsTransitioning && Math.Abs(target - CurrentEyeHeight) < 0.001))
        {
            lock (_lock)
            {
                _transitionTo = target;
                _velocity = 0;
                _snap = true; // a running slide stops here
            }

            CurrentEyeHeight = target;
            _lastHeightWriteUtc = DateTime.UtcNow;
            return SendEyeHeight(target);
        }

        lock (_lock)
        {
            _transitionTo = target;
            _snap = false;

            if (_transitionTask is not { IsCompleted: false })
            {
                _guide = CurrentEyeHeight;
                _guideVelocity = 0;
                _velocity = 0;
                var token = _cts?.Token ?? CancellationToken.None;
                _transitionTask = Task.Run(() => TransitionLoop(token));
            }
        }

        return _sender != null;
    }

    // Heights we sent lately. VRChat echoes every write (each step of a slide too), and those echoes
    // must not be mistaken for you or a new avatar changing the height.
    private static readonly Queue<(double Height, DateTime AtUtc)> _recentHeights = new();

    private static bool SendEyeHeight(double height)
    {
        lock (_recentHeights)
        {
            _recentHeights.Enqueue((height, DateTime.UtcNow));
            while (_recentHeights.Count > 0 && DateTime.UtcNow - _recentHeights.Peek().AtUtc > TimeSpan.FromSeconds(2))
            {
                _recentHeights.Dequeue();
            }
        }

        return Send(EyeHeightAddress, (float)height);
    }

    private static bool WasSentRecently(double height)
    {
        lock (_recentHeights)
        {
            return _recentHeights.Any(h => Math.Abs(h.Height - height) < 0.005 && DateTime.UtcNow - h.AtUtc < TimeSpan.FromSeconds(2));
        }
    }

    private static async Task TransitionLoop(CancellationToken token)
    {
        // Real elapsed time per step, so timer hiccups don't make the motion jerky.
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = clock.Elapsed;
                var delta = Math.Min((now - last).TotalSeconds, 0.1);
                last = now;

                double height;
                bool done;
                lock (_lock)
                {
                    if (_snap) break;

                    height = SmoothStep(CurrentEyeHeight, _transitionTo, ref _velocity, ref _guide, ref _guideVelocity,
                        SmoothTimeFor(Settings.SmoothSeconds), delta);

                    // Close enough and nearly still: land exactly on the target.
                    done = HasSettled(height, _transitionTo, _velocity, _guide);
                    if (done)
                    {
                        height = _transitionTo;
                        _velocity = 0;
                    }
                }

                CurrentEyeHeight = height;
                _lastHeightWriteUtc = DateTime.UtcNow;
                SendEyeHeight(height);

                if (done) break;
                await Task.Delay(TransitionStep, token);
            }
        }
        catch (OperationCanceledException)
        {
        }

        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool Send(string address, object value)
    {
        var sender = _sender;
        if (sender == null) return false;

        try
        {
            var packet = Osc.Encode(address, value);
            sender.Send(packet, packet.Length, Settings.OscHost, Settings.SendPort);
            return true;
        }
        catch (Exception e)
        {
            ActivityLog.Add($"VRChat: couldn't send OSC ({e.Message})", LogLevel.Error);
            return false;
        }
    }

    /// <summary>Expires grow effects and switches avatars back when their time is up.</summary>
    private static async Task TickLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(250, token);

                int removed, parametersEnded;
                bool reapply;
                lock (_lock)
                {
                    removed = _effects.RemoveAll(e => e.ExpiresUtc <= DateTime.UtcNow);
                    parametersEnded = _parameters.RemoveAll(p => p.ExpiresUtc <= DateTime.UtcNow);
                    reapply = _reapplyAt.RemoveAll(t => t <= DateTime.UtcNow) > 0;
                }

                if (removed > 0) SendHeight();
                if (parametersEnded > 0) SendParameters();
                if (reapply) Reapply();
                if (removed > 0 || parametersEnded > 0) StatusChanged?.Invoke(null, EventArgs.Empty);

                AdvanceAvatarQueue();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task ListenLoop(UdpClient listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await listener.ReceiveAsync(token);
                var wasConnected = IsConnected;
                LastReceivedUtc = DateTime.UtcNow;

                foreach (var message in Osc.Decode(result.Buffer)) HandleIncoming(message);
                if (!wasConnected) StatusChanged?.Invoke(null, EventArgs.Empty);
            }
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            ActivityLog.Add($"VRChat: stopped listening ({e.Message})", LogLevel.Warning);
        }
    }

    private static void HandleIncoming(OscMessage message)
    {
        switch (message.Address)
        {
            case AvatarChangeAddress when message.First is string avatarId:
                // Also arrives (again) after our own switches. Either way the new avatar has loaded
                // fresh, so whatever is active gets sent again.
                CurrentAvatarId = avatarId;
                OnAvatarChanged();
                StatusChanged?.Invoke(null, EventArgs.Empty);
                break;

            case var address when address.StartsWith(ParameterPrefix, StringComparison.Ordinal):
                lock (_lock)
                {
                    if (_seenParameters.Count < 500) _seenParameters.Add(address[ParameterPrefix.Length..]);
                }

                break;

            case EyeHeightAddress when message.First is float height:
                bool growing;
                lock (_lock) growing = _effects.Count > 0;
                // VRChat echoes our writes, including every step of a smooth slide.
                var ourOwnWrite = WasSentRecently(height);
                var justSwitched = DateTime.UtcNow - _avatarChangedUtc < TimeSpan.FromSeconds(20);

                if (growing && justSwitched && !ourOwnWrite && Settings.LearnBaseHeight)
                {
                    // The new avatar loaded at its own height: that's the new normal, grow from there
                    // (sliding up from the height it loaded at).
                    Settings.BaseEyeHeight = Math.Round(height, 3);
                    SettingsService.SaveSoon();
                    CurrentEyeHeight = height;
                    SendHeight();
                }
                else if (!growing && Settings.LearnBaseHeight && DateTime.UtcNow - _lastHeightWriteUtc > TimeSpan.FromSeconds(2) &&
                         Math.Abs(height - Settings.BaseEyeHeight) > 0.005)
                {
                    // You used VRChat's scaling menu (or switched avatars): your new normal height.
                    Settings.BaseEyeHeight = Math.Round(height, 3);
                    SettingsService.SaveSoon();
                    CurrentEyeHeight = height;
                }
                else if (!growing)
                {
                    CurrentEyeHeight = height;
                }

                StatusChanged?.Invoke(null, EventArgs.Empty);
                break;
        }
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
