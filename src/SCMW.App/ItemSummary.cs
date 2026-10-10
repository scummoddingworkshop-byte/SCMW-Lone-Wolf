using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace SCMW.App;

/// <summary>
/// One item in the catalogue: what it is called, its category, a one-line description,
/// an optional picture, whether its values can be changed (the "_ES" idea), and its values.
/// </summary>
public sealed class ItemSummary
{
    public ItemSummary(string name, string category, string description, bool editable,
                       IImage? picture = null, IReadOnlyList<ItemValue>? values = null,
                       string? assetId = null)
    {
        Name = name;
        Category = category;
        Description = description;
        Editable = editable;
        Picture = picture;
        Values = values ?? Array.Empty<ItemValue>();
        AssetId = assetId;
    }

    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public bool Editable { get; }
    public IImage? Picture { get; }

    /// <summary>The real content asset id (the reader's load key); null for a sample item.</summary>
    public string? AssetId { get; }
    public IReadOnlyList<ItemValue> Values { get; private set; }

    /// <summary>Replaces the values (real items load their fields after the list is built).</summary>
    public void SetValues(IReadOnlyList<ItemValue> values) => Values = values;

    /// <summary>The cached icon PNG path once extracted, or null (the row shows a letter tile).</summary>
    public string? IconPath { get; set; }
}
