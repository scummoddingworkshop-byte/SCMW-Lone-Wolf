namespace SCMW.Core.Editing;

/// <summary>
/// A saved snapshot of one project's change list at a moment in time. Versions live inside the
/// project folder (never the game files), so a player can always return to an earlier set of edits.
/// <see cref="Id"/> is the version file's name without its extension (a UTC timestamp), and is the
/// stable handle used by <see cref="IChangeStore.Restore"/>.
/// </summary>
/// <param name="Id">Stable version id (the file name stem, e.g. <c>20260927-143012</c>).</param>
/// <param name="Label">Free-form player label; empty when the player chose not to name it.</param>
/// <param name="CreatedUtc">When the version was saved, in UTC.</param>
/// <param name="ChangeCount">How many change records the version contains.</param>
public sealed record ProjectVersion(string Id, string Label, DateTime CreatedUtc, int ChangeCount);
