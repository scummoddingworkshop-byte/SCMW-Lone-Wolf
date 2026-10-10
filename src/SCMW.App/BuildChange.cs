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
    public BuildChange(string itemName, IReadOnlyList<BuildValueChange> values)
    {
        ItemName = itemName;
        Values = values;
    }

    /// <summary>The item's recorded name — what the game files call it.</summary>
    public string ItemName { get; }

    /// <summary>Every value changed on this item.</summary>
    public IReadOnlyList<BuildValueChange> Values { get; }

    /// <summary>The item's picture once resolved; null shows the fallback mark.</summary>
    [ObservableProperty] private IImage? _picture;
}

/// <summary>One changed value on an item: its friendly name and the plain "old → new" line.</summary>
public sealed record BuildValueChange(string ValueName, string OldValue, string NewValue)
{
    /// <summary>Plain "10 → 25" line.</summary>
    public string Summary => $"{OldValue} → {NewValue}";
}
