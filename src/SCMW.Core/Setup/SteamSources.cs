namespace SCMW.Core.Setup;

/// <summary>
/// Real filesystem-backed sources. Steam root discovery uses well-known install
/// locations (registry enumeration is reserved for a later task; the locator is
/// source-agnostic and fakes cover all logic in tests).
/// </summary>
public sealed class SteamSources : IScumSources
{
    public static SteamSources Default { get; } = new();

    public IReadOnlyList<string> SteamRoots()
    {
        var roots = new List<string>();
        foreach (var baseDir in WellKnownBaseDirs())
        {
            var candidate = Path.Combine(baseDir, "Steam");
            if (Directory.Exists(Path.Combine(candidate, "steamapps")))
                roots.Add(candidate);
        }
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
            return null; // unreadable/volatile file = treat as absent
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    private static IEnumerable<string> WellKnownBaseDirs()
    {
        foreach (var name in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }
}
