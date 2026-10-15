using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>
/// One row in Build's "What's in this mod": an item, every value changed on it, and its picture
/// (resolved after the row is built). A plain class (not a record) so the picture can arrive late.
/// </summary>
public partial class BuildChange : ObservableObject
{
    public BuildChange(string itemName, IReadOnlyList<BuildValueChange> values, string? path = null)
    {
        ItemName = itemName;
        Values = values;
        Path = path ?? itemName;
    }

    /// <summary>The item's recorded name — what the game files call it.</summary>
    public string ItemName { get; }

    /// <summary>Every value changed on this item.</summary>
    public IReadOnlyList<BuildValueChange> Values { get; }

    /// <summary>The item's full game path (the record's path, or its recorded name when none).</summary>
    public string Path { get; }

    /// <summary>The item's friendly name (from its path), shown as the row heading.</summary>
    [ObservableProperty] private string _friendlyName = "";

    /// <summary>The item's picture once resolved; null shows the fallback mark.</summary>
    [ObservableProperty] private IImage? _picture;

    /// <summary>True when the row has a picture to draw (else the fallback mark shows).</summary>
    public bool HasPicture => Picture is not null;

    partial void OnPictureChanged(IImage? value) => OnPropertyChanged(nameof(HasPicture));
}

/// <summary>One changed value on an item: its friendly name and the plain "old → new" line.</summary>
public sealed record BuildValueChange(string ValueName, string OldValue, string NewValue)
{
    /// <summary>Plain "10 → 25" line.</summary>
    public string Summary => $"{OldValue} → {NewValue}";
}
