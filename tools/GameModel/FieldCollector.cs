using SCMW.Core.Content;

namespace SCMW.GameModel;

/// <summary>One row of a family's field table: a field, its type, and an example value.</summary>
/// <param name="Name">The field name (a plain name, or a dotted path for a nested value).</param>
/// <param name="Type">The game's UE property type, e.g. <c>FloatProperty</c>.</param>
/// <param name="Assets">How many of the family's assets carry the field.</param>
/// <param name="Nested">True for a value read from inside a struct or list.</param>
/// <param name="Example">One example value, for the reader's eye (not the full range).</param>
internal sealed record FieldRow(string Name, string Type, int Assets, bool Nested, string Example);

/// <summary>
/// Folds the fields of many assets into one table: how many carry each field, its
/// type, whether it is nested, and one example value. Pure — no game needed.
/// </summary>
internal static class FieldCollector
{
    internal static IReadOnlyList<FieldRow> Collect(IEnumerable<IReadOnlyList<AssetField>> perAsset)
    {
        var acc = new Dictionary<string, Counts>(StringComparer.Ordinal);

        foreach (var fields in perAsset)
        {
            // One asset counts once per field, even if it reports the name twice.
            var seenThisAsset = new HashSet<string>(StringComparer.Ordinal);

            foreach (var field in fields)
            {
                var name = field.TechnicalName;
                if (string.IsNullOrEmpty(name) || !seenThisAsset.Add(name)) continue;

                if (!acc.TryGetValue(name, out var counts))
                    acc[name] = counts = new Counts();

                counts.Assets++;
                if (counts.Type.Length == 0 && !string.IsNullOrEmpty(field.ValueTypeHint))
                    counts.Type = field.ValueTypeHint!;
                if (counts.Example.Length == 0 && !string.IsNullOrEmpty(field.CurrentValue))
                    counts.Example = field.CurrentValue;
                counts.Nested |= field.Nested;
            }
        }

        return acc
            .Select(pair => new FieldRow(pair.Key, pair.Value.Type, pair.Value.Assets, pair.Value.Nested, pair.Value.Example))
            .OrderByDescending(row => row.Assets)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class Counts
    {
        public int Assets;
        public string Type = string.Empty;
        public string Example = string.Empty;
        public bool Nested;
    }
}
