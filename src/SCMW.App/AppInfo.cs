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

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var value = info?.Split('+')[0];
        return string.IsNullOrWhiteSpace(value) ? "1.0.0" : value;
    }
}
