namespace SCMW.Core.Setup;

/// <summary>Outcome of SCUM install detection.</summary>
/// <param name="Path">Verified install dir, e.g. <c><library>\common\SCUM</c>, or null.</param>
public sealed record LocatorResult(bool Found, string? Path, string BuildId, string Hint);

/// <summary>Filesystem-ish abstraction so detection stays pure and fakeable in tests.</summary>
public interface IScumSources
{
    IReadOnlyList<string> SteamRoots();
    string? ReadAcf(string steamAppsDir, string fileName);
    bool FileExists(string path);
}

/// <summary>
/// SCUM install detection, pure logic: steam roots -> per-root libraryfolders.vdf paths
/// (root itself is also a library) -> appmanifest_513710.acf -> installdir ->
/// <c><library>\steamapps\common\<installdir></c> -> marker verify <c><candidate>\SCUM\Binaries\Win64\SCUM.exe</c>.
/// First verified candidate wins; manifests without the executable marker are never trusted.
/// </summary>
public static class ScumLocator
{
    private const int ScumAppId = 513710;
    private const string LibrariesVdfFile = "libraryfolders.vdf";
    private static readonly string ManifestFile = $"appmanifest_{ScumAppId}.acf";
    // Real SCUM layout: <library>\common\SCUM\SCUM\Binaries\Win64\SCUM.exe — the exe sits one
    // level deeper than the candidate (installdir "SCUM"), so the marker is relative to the
    // candidate dir and starts with a second "SCUM" segment.
    private const string MarkerSubPath = "SCUM/Binaries/Win64/SCUM.exe";
    private const string BrowseHint =
        "SCUM install not found. Browse to the SCUM install folder manually to configure it.";

    public static LocatorResult Locate(IScumSources src)
    {
        var roots = src.SteamRoots();
        if (roots.Count == 0)
            return NotFound(manifestSeenUnverified: false);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestSeenUnverified = false;

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            // The root is itself a library; libraries.vdf adds any further ones.
            var libraries = new List<string> { root };
            var vdf = src.ReadAcf(Join(root, "steamapps"), LibrariesVdfFile);
            if (vdf is not null)
                foreach (var path in AcfParser.ReadValues(vdf, "path"))
                    if (!string.IsNullOrWhiteSpace(path))
                        libraries.Add(path);

            foreach (var library in libraries)
            {
                if (!visited.Add(library)) continue;

                var manifest = src.ReadAcf(Join(library, "steamapps"), ManifestFile);
                if (manifest is null) continue;

                var installDir = AcfParser.ReadValue(manifest, "installdir");
                if (string.IsNullOrWhiteSpace(installDir)) continue;

                // Real Steam layout: <library>\steamapps\common\<installdir>. The manifest lives in
                // <library>\steamapps, and the game one level deeper under "common".
                var candidate = $"{Join(Join(library, "steamapps"), "common")}/{installDir.Trim('/', '\\')}";
                if (VerifyInstalledDir(candidate, src))
                    return Found(candidate, manifest);
                manifestSeenUnverified = true;
            }
        }

        return NotFound(manifestSeenUnverified);
    }

    /// <summary>Marker check only: the verified SCUM executable must exist under <paramref name="dir"/>.</summary>
    public static bool VerifyInstalledDir(string dir, IScumSources src) =>
        !string.IsNullOrWhiteSpace(dir) && src.FileExists($"{dir}/{MarkerSubPath}");

    private static LocatorResult Found(string candidate, string manifest) => new(
        true,
        candidate,
        AcfParser.ReadValue(manifest, "buildid") ?? string.Empty,
        $"SCUM (AppID {ScumAppId}) install verified at {candidate}.");

    private static LocatorResult NotFound(bool manifestSeenUnverified) => new(
        false,
        null,
        string.Empty,
        manifestSeenUnverified
            ? "A SCUM install was located via its Steam appmanifest, but the executable marker was missing, so it cannot be trusted. Browse to the real SCUM install folder manually to configure it."
            : BrowseHint);

    private static string Join(string left, string right) => $"{left.TrimEnd('/', '\\')}/{right}";
}
