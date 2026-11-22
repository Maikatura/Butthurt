using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Butthurt.VRChat.Models;

namespace Butthurt.VRChat.Services;

public record AvatarParameterInfo(string Name, ParameterType Type, bool Writable);

public record AvatarInfo(string Id, string Name, DateTime LastUsedUtc, IReadOnlyList<AvatarParameterInfo> Parameters)
{
    /// <summary>Parameters OSC can change (VRChat's own read-only ones like VelocityX are left out).</summary>
    public IEnumerable<AvatarParameterInfo> WritableParameters => Parameters.Where(p => p.Writable);
}

/// <summary>
/// Every avatar you've worn with OSC on, read from the files VRChat writes to
/// AppData\LocalLow\VRChat\VRChat\OSC\{user}\Avatars\{avatar}.json: names and all their parameters.
/// </summary>
public static class AvatarLibrary
{
    private static readonly object _lock = new();
    private static List<AvatarInfo> _avatars = new();
    private static DateTime _loadedFolderStamp;
    private static DateTime _checkedUtc;

    public static string OscFolder => Path.Combine(
        Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!,
        "LocalLow", "VRChat", "VRChat", "OSC");

    /// <summary>All known avatars, most recently worn first. Re-read when VRChat writes new files.</summary>
    public static IReadOnlyList<AvatarInfo> All
    {
        get
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _checkedUtc > TimeSpan.FromSeconds(3))
                {
                    _checkedUtc = DateTime.UtcNow;
                    var stamp = FolderStamp();
                    if (stamp != _loadedFolderStamp)
                    {
                        _avatars = Load();
                        _loadedFolderStamp = stamp;
                    }
                }

                return _avatars;
            }
        }
    }

    public static AvatarInfo? Get(string? avatarId) =>
        string.IsNullOrWhiteSpace(avatarId)
            ? null
            : All.FirstOrDefault(a => string.Equals(a.Id, avatarId.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>"Student" for a known avatar, otherwise a shortened ID.</summary>
    public static string DisplayName(string? avatarId)
    {
        if (Get(avatarId) is { } avatar && avatar.Name.Length > 0) return avatar.Name;
        if (string.IsNullOrWhiteSpace(avatarId)) return "Unknown";
        return avatarId.Length > 18 ? avatarId[..18] + "…" : avatarId;
    }

    /// <summary>Reads one avatar file. Public for the tests.</summary>
    public static AvatarInfo? Parse(string json, DateTime lastUsedUtc)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(id)) return null;

        var parameters = new List<AvatarParameterInfo>();
        if (root.TryGetProperty("parameters", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var parameter in list.EnumerateArray())
            {
                var name = parameter.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;

                var writable = parameter.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object;
                var typeSource = writable ? input : parameter.TryGetProperty("output", out var output) ? output : default;
                var type = typeSource.ValueKind == JsonValueKind.Object && typeSource.TryGetProperty("type", out var t)
                    ? t.GetString() switch
                    {
                        "Bool" => ParameterType.Bool,
                        "Int" => ParameterType.Int,
                        _ => ParameterType.Float
                    }
                    : ParameterType.Float;

                parameters.Add(new AvatarParameterInfo(name, type, writable));
            }
        }

        var avatarName = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
        return new AvatarInfo(id, avatarName, lastUsedUtc, parameters.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static List<AvatarInfo> Load()
    {
        var avatars = new List<AvatarInfo>();
        try
        {
            if (!Directory.Exists(OscFolder)) return avatars;

            foreach (var file in Directory.EnumerateFiles(OscFolder, "avtr_*.json", SearchOption.AllDirectories))
            {
                try
                {
                    if (Parse(File.ReadAllText(file), File.GetLastWriteTimeUtc(file)) is { } avatar &&
                        Guid.TryParse(avatar.Id.Replace("avtr_", ""), out _)) // skips test avatars from the SDK emulator
                    {
                        avatars.Add(avatar);
                    }
                }
                catch
                {
                    // A half-written or odd file, skip it.
                }
            }
        }
        catch
        {
            // No access to the folder, nothing to suggest.
        }

        // The same avatar can show up for several VRChat accounts, keep the newest.
        return avatars.GroupBy(a => a.Id).Select(g => g.OrderByDescending(a => a.LastUsedUtc).First())
            .OrderByDescending(a => a.LastUsedUtc).ToList();
    }

    private static DateTime FolderStamp()
    {
        try
        {
            if (!Directory.Exists(OscFolder)) return DateTime.MinValue;
            // Newest file change (VRChat rewrites an avatar's file when you wear an updated version).
            return Directory.EnumerateFiles(OscFolder, "avtr_*.json", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}
