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

    /// <summary>The paragraph that introduces the app where it needs one (a pack's read-me).</summary>
    public const string About =
        "SCMW — Lone Wolf is a mod manager as well as a mod maker. Drag a .pak into it to install one " +
        "from anywhere, and switch mods on and off or remove them, all in one place. If you are not " +
        "using a mod manager yet (or you would like to try something different) the latest version " +
        "is on my Nexus page:";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var value = info?.Split('+')[0];
        return string.IsNullOrWhiteSpace(value) ? "1.0.0" : value;
    }
}
