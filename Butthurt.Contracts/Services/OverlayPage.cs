using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Butthurt.Contracts.Services;

/// <summary>
/// The files of the page OBS shows. They live in the "overlay" folder next to the app so anyone can restyle it;
/// they're read on every request, so an edit shows after refreshing the Browser source. The original files are
/// also built into the app: a missing folder is put back on start and a missing file is served from the built-in copy.
/// </summary>
internal static class OverlayPage
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "overlay");

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".webm"] = "video/webm",
        [".mp4"] = "video/mp4"
    };

    private const string ResourcePrefix = "overlay.";

    /// <summary>Writes the original files when the folder is gone (first start, or someone deleted it to reset).</summary>
    public static void EnsureFolder()
    {
        if (Directory.Exists(Folder)) return;

        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var name in BuiltInNames())
            {
                using var source = BuiltIn(name)!;
                using var target = File.Create(Path.Combine(Folder, name));
                source.CopyTo(target);
            }
        }
        catch (Exception e)
        {
            // Read-only install folder and the like: the built-in copy is served instead.
            ActivityLog.Add($"Couldn't write the overlay folder, using the built-in overlay: {e.Message}", LogLevel.Warning);
        }
    }

    /// <summary>The file for a request path ("/" is index.html), from the folder or else the built-in copy.</summary>
    public static (byte[] Body, string ContentType)? Load(string requestPath)
    {
        var name = Uri.UnescapeDataString(requestPath).TrimStart('/');
        if (name.Length == 0) name = "index.html";

        var contentType = ContentTypes.GetValueOrDefault(Path.GetExtension(name), "application/octet-stream");

        // Only files inside the folder, never "../something".
        var root = Path.GetFullPath(Folder) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, name));
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
        {
            try
            {
                return (File.ReadAllBytes(full), contentType);
            }
            catch (IOException)
            {
                // Probably mid-save in an editor; fall back for this one request.
            }
        }

        using var builtIn = BuiltIn(name);
        if (builtIn == null) return null;
        using var memory = new MemoryStream();
        builtIn.CopyTo(memory);
        return (memory.ToArray(), contentType);
    }

    private static IEnumerable<string> BuiltInNames() =>
        typeof(OverlayPage).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix))
            .Select(n => n[ResourcePrefix.Length..]);

    private static Stream? BuiltIn(string name) =>
        name.Contains('/') || name.Contains('\\')
            ? null
            : typeof(OverlayPage).Assembly.GetManifestResourceStream(ResourcePrefix + name);
}
