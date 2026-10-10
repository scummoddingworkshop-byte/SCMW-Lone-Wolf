using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SCMW.Core.Setup;

namespace SCMW.App.Setup;

/// <summary>
/// Real Windows <see cref="IScumSources"/>: Steam install roots from the registry
/// (HKCU steam "SteamPath", HKLM WOW6432Node steam "InstallPath") plus the same
/// filesystem probes the Core locator expects. Registry access is best-effort:
/// unavailable / denied / non-Windows degrades to no roots rather than throwing.
/// </summary>
public sealed class WindowsScumSources : IScumSources
{
    public static WindowsScumSources Default { get; } = new();

    public IReadOnlyList<string> SteamRoots()
    {
        var roots = new List<string>();
        if (!OperatingSystem.IsWindows())
            return roots; // registry is Windows-only; degrade to no roots elsewhere

        // Per-user install first (the common case), then the machine-wide install.
        AddRootIfSteam(roots,
            ReadRegistryValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
        AddRootIfSteam(roots,
            ReadRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
        AddRootIfSteam(roots,
            ReadRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));

        return roots;
    }

    public string? ReadAcf(string steamAppsDir, string fileName)
    {
        try
        {
            var path = Path.Combine(steamAppsDir, fileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null; // unreadable / volatile file = treat as absent
        }
    }

    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void AddRootIfSteam(List<string> roots, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        // Normalize to the locator's forward-slash convention so the returned
        // candidate renders cleanly (real Windows I/O accepts either separator).
        var normalized = root.Replace('\\', '/').TrimEnd('/');
        if (!Directory.Exists(Path.Combine(normalized, "steamapps")))
            return;

        if (!roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            roots.Add(normalized);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryValue(RegistryHive hive, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception)
        {
            return null; // registry unavailable / access denied -> no root
        }
    }
}
