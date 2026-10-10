// src/SCMW.Core/Knowledge/StatusResolver.cs
// The honesty engine's final word: a player-facing status is never hand-assigned. It is
// derived from (a) whether the field can be written at all and (b) whether a test has
// actually been recorded for THIS asset+field. The only path to Editable is
// Writable + hasTestedFact == true.
namespace SCMW.Core.Knowledge;

/// <summary>Player-facing status. <see cref="Editable"/> requires proven capability AND evidence.</summary>
public enum ResolvedStatus { Editable, Limited, ReadOnly, Unknown }

/// <summary>
/// Resolves a <see cref="WriteCapability"/> plus evidence presence into a
/// <see cref="ResolvedStatus"/>.
///
/// RESOLVE MATRIX:
/// <list type="bullet">
/// <item>Writable + tested -> <see cref="ResolvedStatus.Editable"/></item>
/// <item>Writable + no tested fact -> <see cref="ResolvedStatus.Limited"/></item>
/// <item>NotWritable (either way) -> <see cref="ResolvedStatus.ReadOnly"/></item>
/// <item>Unknown (either way) -> <see cref="ResolvedStatus.Unknown"/></item>
/// </list>
/// <see cref="ResolvedStatus.Editable"/> is impossible without a tested fact: there is no
/// overload or default that can produce it from capability alone.
/// </summary>
public static class StatusResolver
{
    /// <summary>
    /// Honest status for a field. <paramref name="hasTestedFact"/> means a fact recorded
    /// for this exact asset+field (case-insensitive), not a blanket claim about the name.
    /// </summary>
    public static ResolvedStatus Resolve(WriteCapability capability, bool hasTestedFact) =>
        capability switch
        {
            WriteCapability.Writable => hasTestedFact
                ? ResolvedStatus.Editable
                : ResolvedStatus.Limited,
            WriteCapability.NotWritable => ResolvedStatus.ReadOnly,
            _ => ResolvedStatus.Unknown,
        };

    /// <summary>
    /// The single place a resolved status becomes a player-facing <see cref="StatusKind"/>.
    /// The switch is exhaustive: an unrecognised value throws rather than falling through.
    /// This matters because <see cref="ResolvedStatus"/> orders <c>Editable</c> first, so a
    /// <c>default</c> arm would silently leak a false <c>Editable</c> — exactly what the
    /// honesty rules forbid.
    /// </summary>
    public static StatusKind ToStatusKind(ResolvedStatus status) => status switch
    {
        ResolvedStatus.Editable => StatusKind.Editable,
        ResolvedStatus.Limited => StatusKind.Limited,
        ResolvedStatus.ReadOnly => StatusKind.ReadOnly,
        ResolvedStatus.Unknown => StatusKind.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status,
            "Unhandled resolved status."),
    };

    /// <summary>
    /// Convenience composition of the whole honesty path for one field:
    /// <see cref="FieldCapability.For"/> + the evidence answer, then <see cref="Resolve"/>.
    /// Keeps every caller on the same one-way road to a status.
    /// </summary>
    public static ResolvedStatus For(string technicalName, string? valueTypeHint, bool hasTestedFact) =>
        Resolve(FieldCapability.For(technicalName, valueTypeHint), hasTestedFact);
}
