using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.Backend.Pak;

/// <summary>
/// The pure half of the nested-value reader: walk a little tree of property nodes and emit one
/// row per plain value, addressed by a dotted path. Kept free of the game-file library so it can
/// be tested directly; the library adapter lives in <see cref="NestedValueReader"/>.
/// </summary>
internal static class NestedFieldWalker
{
    /// <summary>How far into nested structs we look, so a pathological package cannot spin.</summary>
    internal const int DefaultMaxDepth = 8;

    /// <summary>How many rows one package may contribute, for the same reason.</summary>
    internal const int DefaultMaxFields = 500;

    /// <summary>
    /// Every plain value in <paramref name="nodes"/>, each as a read-only <see cref="AssetField"/>
    /// whose technical name is the dotted path (e.g. <c>a.B.c</c>). Values we cannot show plainly
    /// (references, lists) and empty structs produce no row. Duplicate paths keep the first.
    /// </summary>
    internal static IReadOnlyList<AssetField> Walk(
        IReadOnlyList<NestedProperty> nodes,
        int maxDepth = DefaultMaxDepth,
        int maxFields = DefaultMaxFields)
    {
        var fields = new List<AssetField>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Recurse(IReadOnlyList<NestedProperty> properties, string prefix, int depth)
        {
            if (depth > maxDepth || fields.Count >= maxFields) return;

            foreach (var property in properties)
            {
                if (string.IsNullOrEmpty(property.Name)) continue;

                // An array step ("[0]") joins straight onto its list; a struct step joins with a dot.
                var path = prefix.Length == 0
                    ? property.Name
                    : property.Name.StartsWith('[')
                        ? prefix + property.Name
                        : prefix + "." + property.Name;

                if (property.IsStruct)
                {
                    Recurse(property.Children, path, depth + 1);
                    if (fields.Count >= maxFields) return;
                    continue;
                }

                if (property.Value is null) continue;   // a reference or list we cannot show plainly
                if (!seen.Add(path)) continue;

                fields.Add(new AssetField(
                    path, path, StatusKind.ReadOnly, property.Value, property.TypeHint, Nested: true));

                if (fields.Count >= maxFields) return;
            }
        }

        Recurse(nodes, string.Empty, 0);
        return fields;
    }
}
