namespace SCMW.GameModel;

/// <summary>
/// Reads a field value and works out which family (if any) it points at. Field
/// values that reference an asset come in a few shapes:
/// <c>Class'/Game/.../Name.Name_C'</c>, <c>SCUM/Content/.../Name.Name</c>, or a bare
/// name such as <c>CI_Metal_Scrap</c>. The target's package path is passed through
/// the same family rules as the walk, so a reference to a chopping recipe or a
/// crafting tag lands in the right family. Anything else (art, engine scripts,
/// plain text) is not a family link.
/// </summary>
internal static class ReferenceFinder
{
    internal static string? Target(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value;

        // A typed reference: look inside the quotes.
        var first = text.IndexOf('\'');
        var last = text.LastIndexOf('\'');
        if (first >= 0 && last > first)
            text = text[(first + 1)..last];

        // Keep the package part of a path (drop a trailing ".ObjectName"/".ObjectName_C").
        var candidate = text;
        var hadPath = false;
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            hadPath = true;
            var tail = text[(slash + 1)..];
            var dot = tail.IndexOf('.');
            if (dot >= 0)
                candidate = text[..(slash + 1)] + tail[..dot];
        }
        else
        {
            var dot = text.IndexOf('.');
            if (dot >= 0)
                candidate = text[..dot];
        }

        if (!candidate.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            candidate += ".uasset";

        var family = FamilyRules.FamilyOf(candidate);

        // A bare name (no path) that only matches the ST_ rule is ambiguous — string-table
        // namespaces start with ST_ too (e.g. "ST_Deena") — so require a path for a link.
        if (family == "string_tables" && !hadPath) return null;

        return family;
    }
}
