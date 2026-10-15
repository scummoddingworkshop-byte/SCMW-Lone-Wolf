using System;
using System.Collections.Generic;
using System.Linq;
using SCMW.Core.Content;
using SCMW.Core.Editing;

namespace SCMW.App;

/// <summary>
/// One home for "what will this project change?", read from its records: one row per changed file,
/// each field shown as the value it had <em>before</em> → the value it will have. A multiply record
/// is worked out to its finished number, so the preview says what the game will actually load.
/// Build and Preview both read this, so the two can never drift.
/// </summary>
public static class ChangeDiff
{
    /// <summary>
    /// One row per changed item (matched by path when it has one, so same-named files stay apart).
    /// A change made against an item's class file joins the item file, so one item is one card.
    /// </summary>
    public static IReadOnlyList<BuildChange> Build(IEnumerable<ChangeRecord> records) =>
        records
            .GroupBy(r => ItemKey(r.AssetPath ?? r.AssetId), StringComparer.OrdinalIgnoreCase)
            .Select(g => new BuildChange(
                ItemNameOf(g),
                g.GroupBy(r => r.FieldTechnicalName)
                    .Select(FieldRow)
                    .ToList(),
                g.Key))
            .ToList();

    /// <summary>
    /// The item a record belongs to: a change made against an item's class file — the "_ES"-less
    /// path, where the game keeps item-level values like the auto-destruct timer — belongs with the
    /// item file itself, so an item with several changes shows as one card, not two.
    /// </summary>
    internal static string ItemKey(string path)
    {
        if (path.EndsWith("_ES", StringComparison.OrdinalIgnoreCase)) return path;

        // Only a real game path can be a class file; a bare recorded name has no file behind it.
        var isPath = path.Contains('/') || path.Contains('\\');
        return isPath ? path + "_ES" : path;
    }

    /// <summary>The item file's own name for the card's heading (a class file is only a donor).</summary>
    private static string ItemNameOf(IGrouping<string, ChangeRecord> group) =>
        group.FirstOrDefault(r => (r.AssetPath ?? r.AssetId)
                .EndsWith("_ES", StringComparison.OrdinalIgnoreCase))?.AssetId
        ?? group.First().AssetId;

    /// <summary>
    /// One field's diff. The value before is the field's game value (the first record's old value);
    /// the value after is the newest record: a typed value, an entry added/removed, or a ×factor
    /// worked out from the game's own number (so the newest change always wins).
    /// </summary>
    private static BuildValueChange FieldRow(IGrouping<string, ChangeRecord> field)
    {
        var first = field.First();
        var last = field.Last();
        var before = first.Operation == ChangeOperation.AddField ? "(not set)" : (first.OldValue ?? "");

        var after = last.Operation switch
        {
            ChangeOperation.Multiply => WorkOutMultiply(before, last.NewValue),
            ChangeOperation.AddEntry => "added",
            ChangeOperation.RemoveEntry => "removed",
            _ => last.NewValue,
        };

        return new BuildValueChange(FieldNames.Friendly(field.Key), before, after);
    }

    /// <summary>Apply a stored factor to the field's game value; keep the factor if it cannot be read.</summary>
    private static string WorkOutMultiply(string? before, string factor)
    {
        if (!Multiplier.TryParse(factor, out var value)) return factor;
        return Multiplier.Apply(before, value) ?? factor;
    }
}
