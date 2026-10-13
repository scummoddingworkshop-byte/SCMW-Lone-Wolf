namespace SCMW.Core.Editing;

/// <summary>
/// How a recorded change applies to an asset: set a value, or add/remove an entry of a list
/// (a recipe's ingredient slot). A list change is a *sequence* — the order it was recorded in
/// matters — so it is never collapsed to a single "latest value" the way a value edit is.
/// </summary>
public enum ChangeOperation
{
    /// <summary>Set a value (the ordinary edit).</summary>
    Set,

    /// <summary>
    /// Multiply the field's game value by a factor ("×2", "half", "+50%"). The factor is stored in
    /// <see cref="ChangeRecord.NewValue"/>; it always scales the file's own value, so a newer value
    /// change replaces the result outright (the newest change wins).
    /// </summary>
    Multiply,

    /// <summary>Add a new entry to a list, cloned from the last existing entry.</summary>
    AddEntry,

    /// <summary>Remove the entry named by the field's list index.</summary>
    RemoveEntry,
}
