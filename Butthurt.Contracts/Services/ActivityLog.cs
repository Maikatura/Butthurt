using System;
using System.Collections.Generic;

namespace Butthurt.Contracts.Services;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
    Event
}

public record LogEntry(DateTime Time, string Message, LogLevel Level);

/// <summary>
/// Small in-memory log shown on the home page. Can be written to from any thread,
/// subscribers are responsible for hopping to the UI thread.
/// </summary>
public static class ActivityLog
{
    private const int MaxEntries = 200;
    private static readonly List<LogEntry> _entries = new();
    private static readonly object _lock = new();

    public static event EventHandler<LogEntry>? EntryAdded;

    public static IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock) return _entries.ToArray();
        }
    }

    public static void Add(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(DateTime.Now, message, level);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        }

        Console.WriteLine($"[{entry.Time:HH:mm:ss}] {level}: {message}");
        EntryAdded?.Invoke(null, entry);
    }
}
