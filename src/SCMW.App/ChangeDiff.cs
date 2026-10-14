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
    /// <summary>One row per changed file (matched by path when it has one, so same-named files stay apart).</summary>
    public static IReadOnlyList<BuildChange> Build(IEnumerable<ChangeRecord> records) =>
        records
            .GroupBy(r => r.AssetPath ?? r.AssetId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BuildChange(
                g.First().AssetId,
                g.GroupBy(r => r.FieldTechnicalName)
                    .Select(FieldRow)
                    .ToList()))
            .ToList();

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
