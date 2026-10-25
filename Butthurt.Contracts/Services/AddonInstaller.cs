using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Butthurt.Contracts.Services;

/// <param name="Folder">Folder inside the WoW install, e.g. "_retail_".</param>
/// <param name="AddonSource">Which bundled addon fits this game version (folder under Addon/).</param>
public record WowFlavor(string Folder, string DisplayName, string AddonSource);

/// <summary>
/// Copies the addon that ships next to Butthurt.exe into the WoW AddOns folder.
/// Nothing is downloaded.
/// </summary>
public static class AddonInstaller
{
    public const string AddonName = "Butthurt";

    public static readonly IReadOnlyList<WowFlavor> Flavors = new[]
    {
        new WowFlavor("_retail_", "Retail", "Retail"),
        new WowFlavor("_ptr_", "PTR", "Retail"),
        new WowFlavor("_beta_", "Beta", "Retail"),
        new WowFlavor("_classic_", "Classic", "Classic"),
        new WowFlavor("_classic_era_", "Classic Era", "Classic"),
        new WowFlavor("_anniversary_", "Classic Anniversary", "Classic"),
    };

    public static string BundledAddonRoot => Path.Combine(AppContext.BaseDirectory, "Addon");

    public static string GetBundledAddonPath(WowFlavor flavor) => Path.Combine(BundledAddonRoot, flavor.AddonSource, AddonName);

    public static string GetInstalledAddonPath(string wowPath, WowFlavor flavor) =>
        Path.Combine(wowPath, flavor.Folder, "Interface", "AddOns", AddonName);

    /// <summary>Finds the World of Warcraft root folder (the one containing _retail_ etc).</summary>
    public static string? DetectWowPath()
    {
        var candidates = new List<string>();

        // The most reliable answer: a game that is running right now (Wow.exe lives in the flavor folder).
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.StartsWith("Wow", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetDirectoryName(process.MainModule?.FileName) is { } flavorFolder)
                {
                    candidates.Add(flavorFolder);
                }
            }
            catch
            {
                // Some processes don't let us read their path.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var key in new[]
                     {
                         @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft",
                         @"SOFTWARE\Blizzard Entertainment\World of Warcraft"
                     })
            {
                try
                {
                    if (Registry.LocalMachine.OpenSubKey(key)?.GetValue("InstallPath") is string installPath)
                    {
                        candidates.Add(installPath);
                    }
                }
                catch
                {
                    // No access to the registry, fall back to guessing.
                }
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "World of Warcraft"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files", "World of Warcraft"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "World of Warcraft"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Games", "World of Warcraft"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Battle.net", "World of Warcraft"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "BattleNet", "World of Warcraft"));
        }

        return candidates.Select(NormalizeWowPath).FirstOrDefault(p => p != null && GetFlavors(p).Any());
    }

    /// <summary>Accepts the root folder or one of the flavor folders and returns the root.</summary>
    public static string? NormalizeWowPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var directory = new DirectoryInfo(path.Trim().TrimEnd('\\', '/'));
            if (!directory.Exists) return null;

            if (Flavors.Any(f => f.Folder.Equals(directory.Name, StringComparison.OrdinalIgnoreCase)) && directory.Parent != null)
            {
                return directory.Parent.FullName;
            }

            return directory.FullName;
        }
        catch
        {
            return null;
        }
    }

    public static IEnumerable<WowFlavor> GetFlavors(string wowPath) =>
        Flavors.Where(f => Directory.Exists(Path.Combine(wowPath, f.Folder)));

    public static string? GetBundledVersion(WowFlavor flavor) => ReadTocVersion(GetBundledAddonPath(flavor));

    public static string? GetInstalledVersion(string wowPath, WowFlavor flavor) =>
        ReadTocVersion(GetInstalledAddonPath(wowPath, flavor));

    public static bool IsInstalled(string wowPath, WowFlavor flavor) =>
        File.Exists(Path.Combine(GetInstalledAddonPath(wowPath, flavor), AddonName + ".toc"));

    public static void Install(string wowPath, WowFlavor flavor)
    {
        var source = GetBundledAddonPath(flavor);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"The bundled addon is missing ({source}). Try reinstalling Butthurt.");
        }

        var target = GetInstalledAddonPath(wowPath, flavor);

        // Only ever remove our own addon folder so files from older versions don't linger.
        if (Directory.Exists(target) && Path.GetFileName(target) == AddonName)
        {
            Directory.Delete(target, recursive: true);
        }

        CopyDirectory(source, target);
        ActivityLog.Add($"Installed the addon for {flavor.DisplayName} ({GetBundledVersion(flavor) ?? "unknown version"})", LogLevel.Success);
    }

    /// <summary>
    /// One click install: finds the game (unless a folder is already set) and installs or updates
    /// the addon for every game version in it. Returns the versions that were installed.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">World of Warcraft could not be found.</exception>
    public static List<WowFlavor> AutoInstall()
    {
        var wowPath = NormalizeWowPath(SettingsService.Current.WowPath);
        if (wowPath == null || !GetFlavors(wowPath).Any())
        {
            wowPath = DetectWowPath() ?? throw new DirectoryNotFoundException(
                "Couldn't find World of Warcraft. Pick the folder on the World of Warcraft page.");
            SettingsService.Current.WowPath = wowPath;
            SettingsService.Save();
        }

        var installed = new List<WowFlavor>();
        foreach (var flavor in GetFlavors(wowPath).Where(f => Directory.Exists(GetBundledAddonPath(f))))
        {
            Install(wowPath, flavor);
            installed.Add(flavor);
        }

        return installed;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static string? ReadTocVersion(string addonPath)
    {
        try
        {
            var toc = Path.Combine(addonPath, AddonName + ".toc");
            if (!File.Exists(toc)) return null;

            var line = File.ReadLines(toc).FirstOrDefault(l => l.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase));
            return line?.Substring("## Version:".Length).Trim();
        }
        catch
        {
            return null;
        }
    }
}
