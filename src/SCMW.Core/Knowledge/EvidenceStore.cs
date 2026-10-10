// src/SCMW.Core/Knowledge/EvidenceStore.cs
// The honesty engine's memory: which asset+field combinations have actually been tested
// in game. The store starts EMPTY - no fact is ever invented. Facts live per workspace at
// <workspace>\knowledge\tested-facts.json (camelCase, atomic temp-then-move write).
// A missing or corrupt file is treated as "no tested facts yet" with a plain warning;
// reading or writing never throws at the caller.
using System.Text.Json;
using System.Text.Json.Serialization;
using SCMW.Core.Logging;

namespace SCMW.Core.Knowledge;

/// <summary>
/// One recorded in-game test: this asset's field was changed and observed to work, on a
/// given build, at a given time. Only these unlock <c>Editable</c>.
/// </summary>
/// <param name="AssetId">Technical id/path of the asset whose field was tested.</param>
/// <param name="FieldTechnicalName">Technical property name that was tested.</param>
/// <param name="TestedUtc">When the test succeeded, in UTC.</param>
/// <param name="BuildId">Optional build/game version the test ran against.</param>
/// <param name="Note">Optional plain-language note about the test.</param>
public sealed record TestedFact(
    string AssetId,
    string FieldTechnicalName,
    DateTime TestedUtc,
    string? BuildId,
    string? Note);

/// <summary>
/// Read/record access to tested facts. Asset+field matching is case-insensitive.
/// </summary>
public interface IEvidenceStore
{
    /// <summary>True when a tested fact exists for this exact asset+field (case-insensitive).</summary>
    bool HasTestedFact(string assetId, string field);

    /// <summary>
    /// Record a tested fact. Idempotent per asset+field (case-insensitive): an existing
    /// pair is left untouched. A failed write leaves memory and disk unchanged.
    /// </summary>
    void Record(TestedFact fact);
}

/// <summary>
/// File-backed <see cref="IEvidenceStore"/>: one camelCase <c>tested-facts.json</c> in a
/// workspace's knowledge folder, written atomically (temp file then move) so a failed
/// write can never corrupt or half-write the store. Corrupt or unreadable files are
/// treated as "no tested facts yet" with a plain warning - never a throw.
/// </summary>
public sealed class JsonEvidenceStore : IEvidenceStore
{
    /// <summary>File name inside the workspace knowledge folder.</summary>
    public const string FileName = "tested-facts.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _dir;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<TestedFact> _facts;

    /// <summary>Load (or start) the store in the given knowledge directory.</summary>
    public JsonEvidenceStore(string knowledgeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(knowledgeDirectory);
        _dir = knowledgeDirectory;
        _path = Path.Combine(knowledgeDirectory, FileName);
        _facts = Load();
    }

    /// <summary>
    /// Store for a workspace root, at exactly <c>&lt;workspaceRoot&gt;\knowledge\tested-facts.json</c>.
    /// </summary>
    public static JsonEvidenceStore ForWorkspace(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return new JsonEvidenceStore(Path.Combine(workspaceRoot, "knowledge"));
    }

    /// <summary>Every recorded fact, in the order recorded. Safe to expose to the UI/tests.</summary>
    public IReadOnlyList<TestedFact> All()
    {
        lock (_gate)
            return _facts.ToList();
    }

    /// <inheritdoc />
    public bool HasTestedFact(string assetId, string field)
    {
        if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(field))
            return false;

        lock (_gate)
            return _facts.Any(f => PairMatches(f, assetId, field));
    }

    /// <inheritdoc />
    public void Record(TestedFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        lock (_gate)
        {
            if (_facts.Any(f => PairMatches(f, fact.AssetId, fact.FieldTechnicalName)))
                return; // idempotent per asset+field

            // Commit to memory only after the file write succeeds, so we never claim a fact
            // was recorded when it was not.
            var candidate = new List<TestedFact>(_facts) { fact };
            if (Save(candidate))
                _facts.Add(fact);
        }
    }

    private static bool PairMatches(TestedFact fact, string assetId, string field) =>
        string.Equals(fact.AssetId, assetId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(fact.FieldTechnicalName, field, StringComparison.OrdinalIgnoreCase);

    private List<TestedFact> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new List<TestedFact>();

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<TestedFact>();

            var file = JsonSerializer.Deserialize<FactsFile>(json, JsonOptions);
            return file?.Facts ?? new List<TestedFact>();
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            // A hand-edited/corrupt or locked file must never take the app down: report it
            // plainly and behave as if nothing has been tested yet.
            Log.Warn($"Could not read the tested-facts store at '{_path}' - starting with no tested facts. {ex.Message}");
            return new List<TestedFact>();
        }
    }

    /// <summary>
    /// Write the whole store atomically. Returns false (leaving the previous file intact
    /// and no temp file behind) when the write fails, so the caller can keep memory honest.
    /// </summary>
    private bool Save(List<TestedFact> facts)
    {
        var temp = Path.Combine(_dir, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(_dir);
            var json = JsonSerializer.Serialize(new FactsFile(facts), JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            TryDeleteFile(temp);
            Log.Warn($"Could not save the tested-facts store at '{_path}' - the previous store is unchanged. {ex.Message}");
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
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            // Best effort only: never mask the original failure.
        }
    }

    /// <summary>On-disk shape; the list is wrapped so future fields can be added safely.</summary>
    private sealed record FactsFile(List<TestedFact>? Facts);
}
