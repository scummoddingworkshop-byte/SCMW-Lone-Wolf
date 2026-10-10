// src/SCMW.Backend.Pak/PakMount.cs
// Thin wrapper around CUE4Parse's DefaultFileProvider. Owns the provider's
// lifetime and converts every mount-time failure (bad path, bad key, init
// error) into a false return + plain message instead of an exception. The AES
// key is never logged and never appears in any error string.
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using SCMW.Core.Logging;

namespace SCMW.Backend.Pak;

public sealed class PakMount : IPakMount
{
    /// <summary>
    /// The only text ever surfaced for an unexpected mount failure (invalid
    /// folder, Initialize() blow-up, malformed key): plain English, no raw
    /// exception text, no stack trace, no key material. Shared with
    /// <see cref="PakContentSource"/>'s defensive catch around the seam.
    /// </summary>
    internal const string MountFailedMessage =
        "SCUM's files could not be read from that folder. Check the path in Settings, then try again.";

    private DefaultFileProvider? _provider;

    public bool IsMounted => _provider != null;

    public int FileCount => _provider?.Files.Count ?? 0;

    public IEnumerable<string> FilePaths => _provider?.Files.Keys ?? Enumerable.Empty<string>();

    public bool TryMount(string pakPath, string? aesKey, out string? error)
    {
        error = null;
        try
        {
            _provider?.Dispose();
            _provider = null;

            if (string.IsNullOrWhiteSpace(pakPath) || !Directory.Exists(pakPath))
            {
                // Plain message only — never echo the raw path.
                error = MountFailedMessage;
                return false;
            }

            // CS0618: the (string, SearchOption, bool, VersionContainer) constructor is
            // marked [Obsolete] in CUE4Parse 1.2.2 but is the exact form verified against
            // the owner's real SCUM install (docs/superpowers/notes/plan2-spike-findings.md).
            // Suppressed narrowly here rather than switching to the StringComparer ctor,
            // which the spike did not exercise.
#pragma warning disable CS0618
            var provider = new DefaultFileProvider(
                pakPath, SearchOption.TopDirectoryOnly, true,
                new VersionContainer(EGame.GAME_UE4_27));
#pragma warning restore CS0618

            provider.Initialize();

            if (!string.IsNullOrWhiteSpace(aesKey))
                provider.SubmitKey(new FGuid(), new FAesKey(aesKey));

            _provider = provider;
            return true;
        }
        catch (Exception ex)
        {
            _provider = null;
            // Plain message only — never the key, never raw exception text or
            // a stack trace. The real failure (type + message) goes to the log
            // with any key material scrubbed out (see Describe) and the raw
            // path reduced to its folder name so no filesystem detail leaks.
            Log.Warn($"PakMount: mounting '{FolderName(pakPath)}' failed ({Describe(ex, aesKey)}).");
            error = MountFailedMessage;
            return false;
        }
    }

    /// <summary>
    /// The last path segment of a pak folder path (the folder name only), so the
    /// mount-failure log never carries the raw path. Empty/root-only paths degrade
    /// to a placeholder rather than logging nothing meaningful.
    /// </summary>
    private static string FolderName(string pakPath)
    {
        var trimmed = pakPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? "?" : name;
    }

    /// <summary>
    /// Formats an exception as "TypeName: message" for logging, replacing every
    /// occurrence of the AES key (as given, in any letter case, and with or
    /// without the 0x prefix) so no key material can land in the log file even
    /// if a library exception echoes its input.
    /// </summary>
    internal static string Describe(Exception ex, string? aesKey)
    {
        var message = ex.Message;
        if (!string.IsNullOrWhiteSpace(aesKey))
            message = Log.Scrub(message, aesKey);
        return $"{ex.GetType().Name}: {message}";
    }

    public bool TryLoadPackageObject(string path, out UObject? obj)
    {
        obj = null;
        if (_provider == null) return false;
        try
        {
            var ok = _provider.TryLoadPackageObject(path, out var loaded);
            obj = loaded;
            return ok;
        }
        catch
        {
            obj = null;
            return false;
        }
    }

    /// <summary>
    /// Loads every export object in a package (the item plus its component objects), so fields
    /// that live on a component (e.g. MaxQuantity) can be read too. Never throws.
    /// </summary>
    public bool TryLoadExports(string packagePath, out IReadOnlyList<UObject> exports)
    {
        exports = Array.Empty<UObject>();
        if (_provider == null || string.IsNullOrWhiteSpace(packagePath)) return false;
        try
        {
            var assetPath = packagePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                ? packagePath
                : packagePath + ".uasset";

            if (!_provider.TryLoadPackage(assetPath, out var package)) return false;
            var list = package.GetExports().ToList();
            if (list.Count == 0) return false;
            exports = list;
            return true;
        }
        catch
        {
            exports = Array.Empty<UObject>();
            return false;
        }
    }

    /// <summary>
    /// Reads one package's raw files (`.uasset`, `.uexp`, …) out of the mounted paks, keyed by
    /// file name. Used only by the build machinery, which edits a copy. Returns false (never
    /// throws) when unmounted or the package is absent.
    /// </summary>
    public bool TryReadPackage(string packagePath, out IReadOnlyDictionary<string, byte[]>? files)
    {
        files = null;
        if (_provider == null || string.IsNullOrWhiteSpace(packagePath)) return false;
        try
        {
            var assetPath = packagePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                ? packagePath
                : packagePath + ".uasset";

            var read = _provider.SavePackage(assetPath);
            if (read.Count == 0) return false;

            var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in read) map[Path.GetFileName(pair.Key)] = pair.Value;
            files = map;
            return true;
        }
        catch
        {
            files = null;
            return false;
        }
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _provider = null;
    }
}
