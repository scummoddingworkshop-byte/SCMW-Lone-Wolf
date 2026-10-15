using System.Reflection;

namespace SCMW.App;

/// <summary>
/// The app's name and version, read once from the built assembly (the version is set in
/// <c>SCMW.App.csproj</c>). One source of truth for the header, the window title and Settings.
/// </summary>
public static class AppInfo
{
    public const string Name = "SCMW — Lone Wolf";

    /// <summary>Where players can leave a tip. One place, so the app and any link agree.</summary>
    public const string SupportUrl = "https://ko-fi.com/scmwlonewolf";

    /// <summary>The app's page on Nexus, where players find it and its other releases.</summary>
    public const string NexusUrl = "https://www.nexusmods.com/scum/mods/316";

    /// <summary>The public source code, for anyone who wants to look inside or help.</summary>
    public const string RepositoryUrl = "https://github.com/scummoddingworkshop-byte/SCMW-Lone-Wolf";

    /// <summary>One plain sentence on what the app is, for places that introduce it (a pack's read-me).</summary>
    public const string About =
        "SCMW — Lone Wolf is a mod maker for SCUM. You change an item's values, add values a file " +
        "does not have, and build the result into a .pak. Your game files are never edited in place, " +
        "and every change can be undone.";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var value = info?.Split('+')[0];
        return string.IsNullOrWhiteSpace(value) ? "1.0.0" : value;
    }
}
