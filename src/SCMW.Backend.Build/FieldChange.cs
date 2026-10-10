namespace SCMW.Backend.Build;

/// <summary>One value to write: the game's own (technical) field name and the string to set.</summary>
public sealed record FieldChange(string Field, string NewValue);
