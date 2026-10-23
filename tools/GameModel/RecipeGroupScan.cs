using System.Globalization;
using System.Text.Json;
using SCMW.Backend.Pak;
using SCMW.Core.Content;

namespace SCMW.GameModel;

/// <summary>
/// KEPT TOOL (2026-10-23). Reads every recipe's crafting group and display order from the installed
/// game and writes them as JSON. The app's "Crafting group" card is filled from that file, so re-run
/// this after a SCUM update. Read-only on the game; writes only the JSON path it is given.
///
/// Invariant: a recipe's group is the <c>CraftingCategoryTag.TagName</c> value on its companion
/// <c>CraftingMetadata_RecipeCategory_2</c> object (proven in game, 8.2d), and the order is
/// <c>DisplayPriority</c> (a byte; lower comes first).
/// </summary>
internal static class RecipeGroupScan
{
    private const string GroupField = "CraftingCategoryTag.TagName";
    private const string OrderField = "DisplayPriority";

    internal static int Run(PakMount mount, PakContentSource source, string outPath, string buildId)
    {
        var paths = mount.FilePaths
            .Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileNameWithoutExtension(path)
                .StartsWith("CR_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine($"Recipes: {paths.Length:N0} files named CR_* found.");

        var rows = new List<Row>(paths.Length);
        var noGroup = 0;
        var defaultOrder = 0;
        var failed = 0;
        for (var i = 0; i < paths.Length; i++)
        {
            var id = paths[i][..^".uasset".Length];
            try
            {
                var fields = source.FieldsAsync(id).GetAwaiter().GetResult();
                var group = ValueOf(fields, GroupField);
                if (string.IsNullOrWhiteSpace(group))
                {
                    noGroup++;
                    continue;
                }

                // Some recipes carry no DisplayPriority at all. A missing property means the class
                // default, which is 0 — the game really does show those first. Kept at 0, and
                // counted, so the report never hides how many rows are the default rather than set.
                var order = ValueOf(fields, OrderField);
                if (!int.TryParse(order, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority))
                {
                    priority = 0;
                    defaultOrder++;
                }

                rows.Add(new Row(group!, NameOf(id), priority));
            }
            catch (Exception ex)
            {
                // One odd recipe must not stop the whole read.
                failed++;
                Console.Error.WriteLine($"  could not read '{id}' ({ex.GetType().Name})");
            }

            if ((i + 1) % 100 == 0)
                Console.WriteLine($"  {i + 1:N0}/{paths.Length:N0} read");
        }

        var document = new ScanFile(
            GameBuild: buildId,
            ReadOn: DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Recipes: rows);

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var folder = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(outPath, json);

        Console.WriteLine();
        Console.WriteLine($"Wrote {rows.Count:N0} recipes to {outPath}");
        Console.WriteLine($"  no crafting group: {noGroup:N0}   order left at the default 0: {defaultOrder:N0}   unreadable: {failed:N0}");

        // A short report per group, so the owner's menu map can be checked at a glance.
        foreach (var group in rows.GroupBy(row => row.Tag).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var priorities = group.Select(row => row.Priority).OrderBy(p => p).ToArray();
            var duplicates = priorities.Length - priorities.Distinct().Count();
            Console.WriteLine(
                $"  {group.Key}: {group.Count()} recipes, priority {priorities.First()}–{priorities.Last()}" +
                (duplicates > 0 ? $", {duplicates} shared number(s)" : string.Empty));
        }

        return 0;
    }

    private static string? ValueOf(IReadOnlyList<AssetField> fields, string technicalName) =>
        fields.FirstOrDefault(field =>
            string.Equals(field.TechnicalName, technicalName, StringComparison.OrdinalIgnoreCase))?.CurrentValue;

    private static string NameOf(string assetId)
    {
        var slash = assetId.LastIndexOf('/');
        return slash >= 0 ? assetId[(slash + 1)..] : assetId;
    }

    private sealed record Row(string Tag, string Name, int Priority);

    private sealed record ScanFile(string GameBuild, string ReadOn, IReadOnlyList<Row> Recipes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
