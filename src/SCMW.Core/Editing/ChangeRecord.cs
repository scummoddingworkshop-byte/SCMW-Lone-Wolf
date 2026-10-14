namespace SCMW.Core.Editing;

/// <summary>
/// One recorded change to a single asset field. Records are the unit of editing: game files are
/// never modified in place — each edit appends one of these to the owning project's
/// <c>changes.json</c> instead. Every record is individually undoable by <see cref="Id"/>, and the
/// value a field shows is the <see cref="NewValue"/> of the latest record for that asset+field
/// (see <see cref="AppliedValue"/>).
/// </summary>
/// <param name="Id">Stable identity of this change; adding the same id twice is ignored.</param>
/// <param name="ProjectId">Id of the project this change belongs to (the project itself owns the list).</param>
/// <param name="AssetId">Technical id/path of the asset the field lives in.</param>
/// <param name="FieldTechnicalName">Technical property name that was changed.</param>
/// <param name="OldValue">Value the field had before this change (the game value at record time).</param>
/// <param name="NewValue">Value this change sets.</param>
/// <param name="CreatedUtc">When the change was recorded, in UTC.</param>
/// <param name="Status">The field's resolved status at record time.</param>
/// <param name="Backend">Which backend applies this later; <c>"pak"</c> is the built-in SCUM backend.</param>
/// <param name="Target">Which side this change applies to; <c>"client"</c> by default.</param>
/// <param name="Operation">Set a value, multiply it, add/remove a list entry, or add a whole new
/// field (see <see cref="ChangeOperation"/>).</param>
/// <param name="AssetPath">
/// The game file's own path, when known. The build resolves by this instead of the friendly name,
/// so two assets sharing a name can never be confused. Null for records made before paths were kept
/// (the build then falls back to <paramref name="AssetId"/>).
/// </param>
/// <param name="DonorPath">
/// For <see cref="ChangeOperation.AddField"/> only: the game file that already has the field, whose
/// exact shape is copied onto the target at build time. Null for every other operation.
/// </param>
public sealed record ChangeRecord(
    Guid Id,
    string ProjectId,
    string AssetId,
    string FieldTechnicalName,
    string? OldValue,
    string NewValue,
    DateTime CreatedUtc,
    StatusKind Status,
    string Backend = "pak",
    string Target = "client",
    ChangeOperation Operation = ChangeOperation.Set,
    string? AssetPath = null,
    string? DonorPath = null);
