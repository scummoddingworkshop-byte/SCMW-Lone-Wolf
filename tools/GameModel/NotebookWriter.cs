using System.Text;

namespace SCMW.GameModel;

/// <summary>A family's headline counts, for the notebook index.</summary>
internal sealed record FamilySummary(string Family, int Assets, int Fields);

/// <summary>
/// Renders the tool-written Markdown notebook pages. Kept pure so the layout is
/// easy to test and the tool can be re-run at any time.
/// </summary>
internal static class NotebookWriter
{
    internal static string FamilyPage(
        string family, IReadOnlyList<FieldRow> rows, int assetCount, string buildId, string readDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Fields — family `{family}`");
        sb.AppendLine();
        sb.AppendLine($"Read from **{assetCount}** assets. SCUM build **{buildId}** (read {readDate}).");
        sb.AppendLine();
        sb.AppendLine($"Distinct fields: **{rows.Count}**.");
        sb.AppendLine();
        sb.AppendLine("`assets` = how many of the family's assets carry the field. `nested` = the field sits " +
                      "inside a struct or list (its name is a dotted path).");
        sb.AppendLine();
        sb.AppendLine("| field | type | assets | nested | example value |");
        sb.AppendLine("|---|---|---:|---|---|");
        foreach (var row in rows)
        {
            var nested = row.Nested ? "yes" : "";
            sb.AppendLine($"| `{row.Name}` | {Esc(row.Type)} | {row.Assets} | {nested} | {Esc(row.Example)} |");
        }
        return sb.ToString();
    }

    internal static string IndexPage(IReadOnlyList<FamilySummary> families, string buildId, string readDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# The Game Model — what's in SCUM's data");
        sb.AppendLine();
        sb.AppendLine($"The facts below were read from the installed game: SCUM build **{buildId}** (read {readDate}).");
        sb.AppendLine();
        sb.AppendLine("The field tables are written by the reading tool. Re-run it after a SCUM update and diff " +
                      "the notebook to see exactly what moved.");
        sb.AppendLine();
        sb.AppendLine("| family | assets | fields |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var family in families)
            sb.AppendLine($"| [`{family.Family}`](fields/{family.Family}.md) | {family.Assets} | {family.Fields} |");
        return sb.ToString();
    }

    /// <summary>Keeps a value from breaking the Markdown table (pipes, newlines).</summary>
    private static string Esc(string value) => value
        .Replace("\\", "\\\\")
        .Replace("|", "\\|")
        .Replace("\r", " ")
        .Replace("\n", " ");
}
