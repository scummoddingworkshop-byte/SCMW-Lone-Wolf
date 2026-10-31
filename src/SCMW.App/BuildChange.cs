using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SCMW.Core.Editing;

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

    /// <summary>True when this row is a brand-new asset the project creates, rather than an edit to a
    /// game file. Such a row shows even before the asset has any value edits.</summary>
    public bool IsNewAsset { get; set; }

    /// <summary>For a brand-new asset: its own name, so the row is headed by the name and never by its
    /// long game path. Null for an ordinary game file.</summary>
    public string? NewAssetName { get; set; }

    /// <summary>The item's friendly name (from its path), shown as the row heading.</summary>
    [ObservableProperty] private string _friendlyName = "";

    /// <summary>The item's picture once resolved; null shows the fallback mark.</summary>
    [ObservableProperty] private IImage? _picture;

    /// <summary>True when the row has a picture to draw (else the fallback mark shows).</summary>
    public bool HasPicture => Picture is not null;

    /// <summary>"4 changes", or "1 change", beside the item's name — so the size of the mod is visible.</summary>
    public string ChangeCountText
    {
        get
        {
            var count = Values.Count;
            return count switch
            {
                0 => string.Empty,
                1 => "1 change",
                _ => $"{count} changes",
            };
        }
    }    partial void OnPictureChanged(IImage? value) => OnPropertyChanged(nameof(HasPicture));
}

/// <summary>One changed value on an item: its plain name and the plain "old → new" line.</summary>
public sealed record BuildValueChange(string ValueName, string OldValue, string NewValue,
                                      ChangeOperation Operation = ChangeOperation.Set)
{
    /// <summary>
    /// The line shown beside the name: "10 → 25" for a typed value, and a plain word for a list change
    /// (an added or removed ingredient, food or field) where a before-and-after would say nothing.
    /// </summary>
    public string Summary => Operation switch
    {
        ChangeOperation.AddEntry => "added",
        ChangeOperation.RemoveEntry => "removed",
        ChangeOperation.AddField => NewValue,                     // "added, set to 0"
        _ => $"{OldValue} → {NewValue}",
    };
}
