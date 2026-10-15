using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.GameModel;

/// <summary>
/// A SCUM string table's two real members. The app's reader is built for blueprints
/// and never looks at this asset type, so the tool reads it here. The entry map is
/// summarised (a count and a few keys) rather than dumped — the notebook is a
/// catalogue of shape, not a full copy of every value.
/// </summary>
internal static class StringTableFields
{
    private const int SampleEntries = 6;

    internal static IReadOnlyList<AssetField> Build(string tableNamespace, IReadOnlyDictionary<string, string> entries)
    {
        var summary = entries.Count == 0
            ? "0 entries"
            : $"{entries.Count} entries: " + string.Join(", ", entries.Keys.Take(SampleEntries));

        return new[]
        {
            new AssetField(
                "StringTable.TableNamespace", "StringTable.TableNamespace", StatusKind.ReadOnly,
                tableNamespace ?? string.Empty, "TextProperty", Nested: true),
            new AssetField(
                "StringTable.KeysToEntries", "StringTable.KeysToEntries", StatusKind.ReadOnly,
                summary, "Map<Name,Text>", Nested: true),
        };
    }
}
