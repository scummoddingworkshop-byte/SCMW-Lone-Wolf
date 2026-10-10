namespace SCMW.Backend.Build;

/// <summary>One item to change: how the project recorded it, and the values to write.</summary>
public sealed record BuildItem(string RecordedName, IReadOnlyList<FieldChange> Changes);
