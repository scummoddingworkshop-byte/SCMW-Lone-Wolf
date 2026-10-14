using System;
using System.Collections.Generic;
using System.Linq;

namespace SCMW.App;

/// <summary>
/// The one place the browse bar's two filters live — shared by the Items screen and the
/// Workspace so the two can never drift. Search matches a row's name; the category matches the
/// game's own tag word (its <c>GridInventorySortGroup.TagName</c>, carried on the row).
/// </summary>
public static class AssetFilter
{
    /// <summary>The chip names: "All" first, then the categories present, sorted. Never empty.</summary>
    public static IReadOnlyList<string> Categories(IEnumerable<ItemSummary> assets) =>
        new[] { "All" }
            .Concat(assets.Select(a => a.Category).Where(c => c.Length > 0)
                .Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>The rows that pass both filters (name, category).</summary>
    public static IEnumerable<ItemSummary> Apply(IEnumerable<ItemSummary> assets, string? search,
                                                 string? category)
    {
        var query = (search ?? string.Empty).Trim();
        var cat = string.IsNullOrEmpty(category) ? "All" : category;

        return assets.Where(a =>
            (cat == "All" || a.Category == cat) &&
            (query.Length == 0 || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
}
