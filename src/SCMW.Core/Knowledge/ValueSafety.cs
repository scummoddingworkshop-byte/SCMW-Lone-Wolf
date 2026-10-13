// src/SCMW.Core/Knowledge/ValueSafety.cs
// The three-band view of a value, on top of FieldSafety's "is it safe to offer?". Where
// FieldSafety answers yes/no, this splits the "care needed" middle out honestly:
//   Normal  — a plain value (a number, a switch, text): change it freely, no fuss.
//   Noted   — a grey-area value we *can* write but which needs a word of care (a name, or a
//             named choice — the game matches those by spelling).
//   Locked  — structure: a link, a list, a mesh. Changing those can break the item, so we show
//             it but do not offer it.
namespace SCMW.Core.Knowledge;

/// <summary>How a value is offered: an ordinary field, a noted grey-area field, or locked.</summary>
public enum ValueBand { Normal, Noted, Locked }

/// <summary>
/// Classifies a field into the three bands the screen shows (never as colours — as an ordinary
/// field, a field with a note, or a field with a plain reason). Built on <see cref="FieldSafety"/>
/// so the "safe" verdict has one home; this only splits its "unsafe" answer into the grey-area
/// kinds we can still write (a name, an enum, a byte) and the structure we will not touch.
/// </summary>
public static class ValueSafety
{
    /// <summary>The band for a field's technical name and (optional) UE type hint.</summary>
    public static ValueBand For(string? technicalName, string? valueTypeHint)
    {
        // A structural name is locked even if its type looks scalar (a "…Icon" index, say).
        if (FieldSafety.IsStructuralName(technicalName))
            return ValueBand.Locked;

        // If the "can we write it?" engine says no, we must not offer it — whatever the name looks
        // like (a complex type outranks a familiar name). The one exception is a retargetable link.
        if (FieldCapability.For(technicalName, valueTypeHint) == WriteCapability.NotWritable)
        {
            if (FieldSafety.TypeCore(valueTypeHint) == "object" && IsRetargetableLink(technicalName))
                return ValueBand.Noted;
            return ValueBand.Locked;
        }

        // SCUM's item text (Caption, Description, …) is a string-table key, not a plain string:
        // writing a sentence into it makes the game show "<MISSING STRING TABLE ENTRY>". So text is
        // shown but not offered for editing. (A plain StrProperty is fine and stays editable.)
        if (FieldSafety.TypeCore(valueTypeHint) == "text")
            return ValueBand.Locked;

        // Everything FieldSafety already blesses is an ordinary value.
        if (FieldSafety.For(technicalName, valueTypeHint) == EditSafety.Safe)
            return ValueBand.Normal;

        var core = FieldSafety.TypeCore(valueTypeHint);

        // A link (an object reference) is locked — changing what it points at is risky — with one
        // exception: an ingredient's accepted tags, which are exactly what "swap which items" means.
        // Only a hard reference can be retargeted; a soft reference is always locked.
        if (core == "object")
            return IsRetargetableLink(technicalName) ? ValueBand.Noted : ValueBand.Locked;
        if (core == "softobject")
            return ValueBand.Locked;

        // The grey area: a name or a named choice is writable, but the game matches it exactly.
        return core switch
        {
            "name" or "enum" or "byte" => ValueBand.Noted,
            _ => ValueBand.Locked,
        };
    }

    /// <summary>
    /// True for the one kind of link we let the owner retarget: an ingredient slot's accepted
    /// tags (<c>…AllowedTypes[0]</c>). Everything else that is a link stays locked.
    /// </summary>
    internal static bool IsRetargetableLink(string? technicalName) =>
        technicalName is not null &&
        technicalName.Contains("AllowedTypes[", StringComparison.OrdinalIgnoreCase);
}
