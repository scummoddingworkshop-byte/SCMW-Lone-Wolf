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
    string Target = "client");
