// src/SCMW.Core/IPickerItem.cs
// The shared shape every picker's choices have: a plain label to show and to filter on. The app's
// one searchable dropdown filters any list of these, so the rule lives here, once, and is testable
// without a screen.
namespace SCMW.Core;

/// <summary>Something a picker can show and filter by its plain label.</summary>
public interface IPickerItem
{
    /// <summary>The plain words the owner reads and types against.</summary>
    string Label { get; }
}

/// <summary>The one "type to filter" rule: keep the labels containing the typed text, ignoring case.</summary>
public static class PickerFilter
{
    /// <summary>The items whose label contains <paramref name="text"/> (blank shows everything). Never null.</summary>
    public static IReadOnlyList<T> Apply<T>(IReadOnlyList<T>? items, string? text) where T : IPickerItem
    {
        if (items is null || items.Count == 0) return Array.Empty<T>();
        if (string.IsNullOrWhiteSpace(text)) return items;

        var needle = text.Trim();
        var matches = new List<T>();
        foreach (var item in items)
        {
            if (item.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)) matches.Add(item);
        }

        return matches;
    }
}
