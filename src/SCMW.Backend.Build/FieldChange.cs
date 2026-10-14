using SCMW.Core.Editing;

namespace SCMW.Backend.Build;

/// <summary>
/// One change to write: the game's own (technical) field name, the string to set, and how to
/// apply it — a value edit, adding/removing a list entry, or adding a whole new field.
/// </summary>
/// <param name="Field">The game's own (technical) property name.</param>
/// <param name="NewValue">The value to set (a factor for a multiply; empty for a list change).</param>
/// <param name="Operation">How to apply it (see <see cref="ChangeOperation"/>).</param>
/// <param name="DonorPath">
/// For <see cref="ChangeOperation.AddField"/> only: the game file that already has the field, whose
/// exact shape is copied onto the target. Null for every other operation.
/// </param>
public sealed record FieldChange(
    string Field, string NewValue, ChangeOperation Operation = ChangeOperation.Set, string? DonorPath = null);
