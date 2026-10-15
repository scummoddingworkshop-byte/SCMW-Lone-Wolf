using SCMW.Core.Content;

namespace SCMW.GameModel;

/// <summary>One family-to-family link: a source family pointing at a target family.</summary>
/// <param name="From">The family the reference is found in.</param>
/// <param name="To">The family the reference points at.</param>
/// <param name="Refs">How many references were seen.</param>
/// <param name="AssetCount">How many distinct source assets carry at least one.</param>
/// <param name="ExampleField">A field where one was found.</param>
/// <param name="ExampleValue">The value of that field, so the link is self-evident.</param>
internal sealed record FamilyLink(string From, string To, int Refs, int AssetCount, string ExampleField, string ExampleValue);

/// <summary>
/// Builds the family-to-family link map from the references held in the fields the
/// reader collected. Pure — no game needed.
/// </summary>
internal sealed class LinkCollector
{
    private readonly Dictionary<(string From, string To), Edge> _edges = new();

    internal void Add(string sourceFamily, string assetId, IReadOnlyList<AssetField> fields)
    {
        foreach (var field in fields)
        {
            var target = ReferenceFinder.Target(field.CurrentValue);
            if (target is null) continue;

            var key = (sourceFamily, target);
            if (!_edges.TryGetValue(key, out var edge))
                _edges[key] = edge = new Edge();

            edge.Refs++;
            edge.Sources.Add(assetId);
            if (edge.ExampleField.Length == 0)
            {
                edge.ExampleField = field.TechnicalName;
                edge.ExampleValue = field.CurrentValue;
            }
        }
    }

    internal IReadOnlyList<FamilyLink> Rows() => _edges
        .Select(pair => new FamilyLink(
            pair.Key.From, pair.Key.To, pair.Value.Refs, pair.Value.Sources.Count,
            pair.Value.ExampleField, pair.Value.ExampleValue))
        .OrderByDescending(link => link.Refs)
        .ThenBy(link => link.From, StringComparer.Ordinal)
        .ThenBy(link => link.To, StringComparer.Ordinal)
        .ToArray();

    private sealed class Edge
    {
        public int Refs;
        public string ExampleField = string.Empty;
        public string ExampleValue = string.Empty;
        public readonly HashSet<string> Sources = new(StringComparer.Ordinal);
    }
}
