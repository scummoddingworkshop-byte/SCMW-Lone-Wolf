using System;
using Avalonia.Media;

namespace SCMW.App;

/// <summary>
/// One item in the catalogue: what it is called, its category, a one-line description,
/// an optional picture, and whether its values can be changed (the "_ES" idea).
/// </summary>
public sealed class ItemSummary
{
    public ItemSummary(string name, string category, string description, bool editable,
                       IImage? picture = null, string? assetId = null)
    {
        Name = name;
        Category = category;
        Description = description;
        Editable = editable;
        Picture = picture;
        AssetId = assetId;
    }

    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public bool Editable { get; }
    public IImage? Picture { get; }

    /// <summary>The real content asset id (the reader's load key); null for a sample item.</summary>
    public string? AssetId { get; }

    /// <summary>The cached icon PNG path once extracted, or null (the row shows a letter tile).</summary>
    public string? IconPath { get; set; }
}
