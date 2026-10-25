using System.Text.Json;
using System.Text.Json.Serialization;
using SCMW.Core.Logging;

namespace SCMW.Core.Editing;

/// <summary>
/// The crafting-menu sections a single project creates. Like the change list and the new assets,
/// they live in their own file inside the project folder (<c>crafting-sections.json</c>); nothing
/// here touches game files. Lists are per-folder, so two projects can never see each other's sections.
/// </summary>
public interface ICraftingSectionStore
{
    /// <summary>Raised after the list is written (a real add or remove), so screens can re-read.</summary>
    event Action? Changed;

    /// <summary>Every section in the project, in the order it was recorded. Empty when none.</summary>
    IReadOnlyList<CraftingSection> Load(string projectFolder);

    /// <summary>Append one section. Idempotent by <see cref="CraftingSection.Id"/>: a duplicate is
    /// ignored. True when it is (or already was) saved; false on a failed write or a blank folder.</summary>
    bool Add(string projectFolder, CraftingSection section);

    /// <summary>Remove the single section with <paramref name="sectionId"/>; the rest keep their order.</summary>
    void Undo(string projectFolder, Guid sectionId);
}

/// <summary>
/// File-backed <see cref="ICraftingSectionStore"/>: one camelCase <c>crafting-sections.json</c> per
/// project folder, written atomically. A damaged or unreadable file is kept aside and treated as
/// "no sections" with a plain warning — reads and writes never throw at the caller.
/// </summary>
public sealed class CraftingSectionStore : ICraftingSectionStore
{
    /// <summary>File name of the section list inside each project folder.</summary>
    public const string FileName = "crafting-sections.json";

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

    public IReadOnlyList<CraftingSection> Load(string projectFolder)
    {
        if (IsBlankFolder(projectFolder))
            return Array.Empty<CraftingSection>();

        lock (_gate)
        {
            var path = Path.Combine(projectFolder, FileName);
            try
            {
                if (!File.Exists(path))
                    return Array.Empty<CraftingSection>();

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return Array.Empty<CraftingSection>();

                var file = JsonSerializer.Deserialize<SectionsFile>(json, JsonOptions);
                return file?.Sections ?? new List<CraftingSection>();
            }
            catch (JsonException)
            {
                // A damaged file is preserved (never overwritten as if empty) so nothing is lost.
                ArchiveCorrupt(projectFolder, path);
                return Array.Empty<CraftingSection>();
            }
            catch (Exception ex) when (IsExpectedIoFailure(ex))
            {
                Log.Warn($"Could not read the crafting-section list at '{path}' - treating the project as having none. {ex.Message}");
                return Array.Empty<CraftingSection>();
            }
        }
    }

    public bool Add(string projectFolder, CraftingSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        bool saved;
        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return false;

            var sections = Load(projectFolder).ToList();
            if (sections.Any(s => s.Id == section.Id))
                return true;

            sections.Add(section);
            saved = TrySave(projectFolder, sections);
        }

        if (saved) Changed?.Invoke();
        return saved;
    }

    public void Undo(string projectFolder, Guid sectionId)
    {
        bool saved;
        lock (_gate)
        {
            if (IsBlankFolder(projectFolder))
                return;

            var sections = Load(projectFolder).ToList();
            var kept = sections.Where(s => s.Id != sectionId).ToList();
            if (kept.Count == sections.Count)
                return;

            saved = TrySave(projectFolder, kept);
        }

        if (saved) Changed?.Invoke();
    }

    /// <summary>Moves a damaged section list aside (keeping it) so the next save can start clean.</summary>
    private static void ArchiveCorrupt(string projectFolder, string path)
    {
        var kept = Path.Combine(projectFolder,
            $"crafting-sections.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        try
        {
            if (File.Exists(path)) File.Move(path, kept, overwrite: true);
            Log.Warn($"The project's crafting-section list was damaged; it was kept as '{Path.GetFileName(kept)}' and treated as empty.");
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            Log.Warn($"The project's crafting-section list was damaged and could not be kept aside. {ex.Message}");
        }
    }

    private static bool IsBlankFolder(string? projectFolder) =>
        string.IsNullOrWhiteSpace(projectFolder);

    /// <summary>Write the whole list atomically: temp file in the same folder, then move over the target.</summary>
    private static bool TrySave(string projectFolder, List<CraftingSection> sections)
    {
        var path = Path.Combine(projectFolder, FileName);
        var temp = Path.Combine(projectFolder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(projectFolder);
            var json = JsonSerializer.Serialize(new SectionsFile(sections), JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            TryDeleteFile(temp);
            Log.Warn($"Could not save the crafting-section list at '{path}' - the previous list is unchanged. {ex.Message}");
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
    private sealed record SectionsFile(List<CraftingSection>? Sections);
}
