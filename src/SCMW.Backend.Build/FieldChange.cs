using SCMW.Core.Editing;

namespace SCMW.Backend.Build;

/// <summary>
/// One change to write: the game's own (technical) field name, the string to set, and how to
/// apply it — a value edit, or adding/removing a list entry.
/// </summary>
public sealed record FieldChange(string Field, string NewValue, ChangeOperation Operation = ChangeOperation.Set);
