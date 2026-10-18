using System.Text.Json;
using System.Text.Json.Serialization;
using SCMW.Core.Logging;

namespace SCMW.Core.Editing;

/// <summary>
/// The brand-new assets a single project creates. Like the change list, a project's new assets live
/// in their own file inside the project folder (<c>new-assets.json</c>); nothing here touches game
/// files. Lists are per-folder, so two projects can never see each other's new assets.
/// </summary>
public interface INewAssetStore
{
    /// <summary>Raised after the list is written (a real add, replace or remove), so screens can
    /// re-read. A duplicate add or a no-op remove changes nothing, so it stays quiet.</summary>
    event Action? Changed;

    /// <summary>Every new asset in the project, in the order it was recorded. Empty when none.</summary>
    IReadOnlyList<NewAsset> Load(string projectFolder);

    /// <summary>Append one new asset. Idempotent by <see cref="NewAsset.Id"/>: a duplicate is ignored.
    /// True when it is (or already was) saved; false on a failed write or a blank folder.</summary>
    bool Add(string projectFolder, NewAsset asset);

    /// <summary>Remove the single new asset with <paramref name="assetId"/>; the rest keep their order.</summary>
    void Undo(string projectFolder, Guid assetId);

    /// <summary>Replace the new asset with the same <see cref="NewAsset.Id"/> in place (keeping its
    /// position), or append it when absent. True when saved; false on a failed write.</summary>
    bool Replace(string projectFolder, NewAsset asset);
}

/// <summary>
/// File-backed <see cref="INewAssetStore"/>: one camelCase <c>new-assets.json</c> per project folder,
/// written atomically (a temp file is fully written, then moved over the target) so a failed write can
/// never leave a half-written list. A damaged or unreadable file is kept aside and treated as
/// "no new assets" with a plain warning — reads and writes never throw at the caller.
/// </summary>
public sealed class NewAssetStore : INewAssetStore
{
    /// <summary>File name of the new-asset list inside each project folder.</summary>
    public const string FileName = "new-assets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Serialises every read-modify-write on the list, so two edits can't lose one another.</summary>
    private readonly object _gate = new();

    /// <summary>Raised after the list is written, so screens re-read rather than caching.</summary>
    public event Action? Changed;

    public IReadOnlyList<NewAsset> Load(string projectFolder)
    {
        if (IsBlankFolder(projectFolder))
            return Array.Empty<NewAsset>();

        lock (_gate)
        {
            var path = Path.Combine(projectFolder, FileName);
            try
            {
                if (!File.Exists(path))
                    return Array.Empty<NewAsset>();

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return Array.Empty<NewAsset>();

                var file = JsonSerializer.Deserialize<AssetsFile>(json, JsonOptions);
                return file?.Assets ?? new List<NewAsset>();
            }
            catch (JsonException)
            {
                // A damaged file is preserved (never overwritten as if empty) so nothing is lost.
                ArchiveCorrupt(projectFolder, path);
                return Array.Empty<NewAsset>();
            }
            catch (Exception ex) when (IsExpectedIoFailure(ex))
            {
                // A hand-edited/corrupt or locked file must never take the app down: report it plainly
                // and behave as if the project has no new assets yet.
                Log.Warn($"Could not read the new-asset list at '{path}' - treating the project as having none. {ex.Message}");
                return Array.Empty<NewAsset>();
            }
        }
    }

    public bool Add(string projectFolder, NewAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        bool saved;
        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return false; // no project folder -> nothing to append to (never throws)

            var assets = Load(projectFolder).ToList();
            if (assets.Any(a => a.Id == asset.Id))
                return true; // idempotent by Id

            assets.Add(asset);
            saved = TrySave(projectFolder, assets);
        }

        if (saved) Changed?.Invoke();   // raised outside the lock: a listener never runs under it
        return saved;
    }

    public void Undo(string projectFolder, Guid assetId)
    {
        bool saved;
        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return; // no project folder -> nothing to remove (never throws)

            var assets = Load(projectFolder).ToList();
            var kept = assets.Where(a => a.Id != assetId).ToList();
            if (kept.Count == assets.Count)
                return; // nothing matched: leave the file untouched

            saved = TrySave(projectFolder, kept);
        }

        if (saved) Changed?.Invoke();
    }

    public bool Replace(string projectFolder, NewAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        bool saved;
        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return false;

            var assets = Load(projectFolder).ToList();
            var at = assets.FindIndex(a => a.Id == asset.Id);
            if (at >= 0)
                assets[at] = asset;
            else
                assets.Add(asset);

            saved = TrySave(projectFolder, assets);
        }

        if (saved) Changed?.Invoke();
        return saved;
    }

    /// <summary>Moves a damaged new-asset list aside (keeping it) so the next save can start clean.</summary>
    private static void ArchiveCorrupt(string projectFolder, string path)
    {
        var kept = Path.Combine(projectFolder,
            $"new-assets.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        try
        {
            if (File.Exists(path)) File.Move(path, kept, overwrite: true);
            Log.Warn($"The project's new-asset list was damaged; it was kept as '{Path.GetFileName(kept)}' and treated as empty.");
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            Log.Warn($"The project's new-asset list was damaged and could not be kept aside. {ex.Message}");
        }
    }

    /// <summary>
    /// True when <paramref name="projectFolder"/> is null or blank. Every path this store builds needs
    /// a real folder: a null one would make <see cref="Path.Combine(string?,string?)"/> throw, while a
    /// blank one would resolve to a stray relative path. Rejecting it up front keeps the store's
    /// never-throws contract; callers only ever pass a resolved project folder.
    /// </summary>
    private static bool IsBlankFolder(string? projectFolder) =>
        string.IsNullOrWhiteSpace(projectFolder);

    /// <summary>
    /// Write the whole new-asset list atomically: serialise to a temp file in the same folder, then
    /// move it over <c>new-assets.json</c>. If anything fails the temp file is removed, the previous
    /// file is left exactly as it was, and false is returned.
    /// </summary>
    private static bool TrySave(string projectFolder, List<NewAsset> assets)
    {
        var path = Path.Combine(projectFolder, FileName);
        var temp = Path.Combine(projectFolder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(projectFolder);
            var json = JsonSerializer.Serialize(new AssetsFile(assets), JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            TryDeleteFile(temp);
            Log.Warn($"Could not save the new-asset list at '{path}' - the previous list is unchanged. {ex.Message}");
            return false;
        }
    }

    private static bool IsExpectedIoFailure(Exception ex) =>
        ex is JsonException or IOException or UnauthorizedAccessException or System.Security.SecurityException
            or NotSupportedException or ArgumentException;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            // Best effort only: never mask the original failure.
        }
    }

    /// <summary>On-disk shape; wrapped so future fields can be added without a breaking change.</summary>
    private sealed record AssetsFile(List<NewAsset>? Assets);
}
