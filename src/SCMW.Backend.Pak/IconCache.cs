// src/SCMW.Backend.Pak/IconCache.cs
// The on-disk icon cache: a folder of downscaled PNGs plus a plain-text manifest
// of asset ids that definitively have no icon. Asset ids are package paths
// ("SCUM/Content/.../Improvised_Bow") that contain '/', so the PNG file name is a
// sanitised, hash-suffixed form of the id rather than the raw path (which could
// not be a single Windows file name). The mapping is deterministic and shared by
// the extractor and the cache so both always agree on where an icon lives.
using System.Security.Cryptography;
using System.Text;
using SCMW.Core.Content;

namespace SCMW.Backend.Pak;

/// <summary>Maps asset ids to their on-disk icon paths. Shared by cache and extractor.</summary>
public static class IconCachePaths
{
    /// <summary>Manifest of items that loaded fine but have no icon anywhere (never retried).</summary>
    public const string NoIconManifestName = "_no-icon.txt";

    /// <summary>Suffix of an in-progress write; renamed into place only when complete.</summary>
    public const string TempExtension = ".tmp";

    private const int MaxNameLength = 120;

    public static string IconPathFor(string iconsDirectory, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return Path.Combine(iconsDirectory, FileNameFor(assetId));
    }

    public static string ManifestPathFor(string iconsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconsDirectory);
        return Path.Combine(iconsDirectory, NoIconManifestName);
    }

    /// <summary>
    /// A single, deterministic file name for an asset id: path separators and any
    /// other invalid characters become '_', the name is length-capped, and a
    /// 12-hex-digit hash of the full id guarantees uniqueness even after capping
    /// (8 hex digits left a small birthday-collision risk across thousands of ids).
    /// </summary>
    public static string FileNameFor(string assetId)
    {
        var builder = new StringBuilder(assetId.Length + 16);
        foreach (var ch in assetId)
            builder.Append(IsSafe(ch) ? ch : '_');

        var name = builder.ToString();
        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetId)))
            .ToLowerInvariant()[..12];
        return $"{name}__{hash}.png";
    }

    private static bool IsSafe(char ch)
        => ch is not ('/' or '\\') && Array.IndexOf(Path.GetInvalidFileNameChars(), ch) < 0;
}

/// <summary>
/// A file-backed <see cref="IIconCache"/> rooted at a caller-supplied folder.
/// PNGs sit directly in that folder; the no-icon manifest is a sibling file.
/// </summary>
public sealed class FileIconCache : IIconCache
{
    public FileIconCache(string iconsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconsDirectory);
        IconsDirectory = iconsDirectory;
    }

    public string IconsDirectory { get; }

    public string? GetIconPath(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            return null;

        var path = IconCachePaths.IconPathFor(IconsDirectory, assetId);
        return File.Exists(path) ? path : null;
    }

    public int Count => EnumeratePngs().Count();

    public long TotalBytes => EnumeratePngs().Sum(TryFileLength);

    public void Clear()
    {
        if (!Directory.Exists(IconsDirectory))
            return;

        foreach (var png in EnumeratePngs())
            TryDelete(png);

        foreach (var temp in Directory.EnumerateFiles(IconsDirectory, "*" + IconCachePaths.TempExtension))
            TryDelete(temp);

        TryDelete(IconCachePaths.ManifestPathFor(IconsDirectory));
    }

    private IEnumerable<string> EnumeratePngs()
        => Directory.Exists(IconsDirectory)
            ? Directory.EnumerateFiles(IconsDirectory, "*.png")
            : Enumerable.Empty<string>();

    private static long TryFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
