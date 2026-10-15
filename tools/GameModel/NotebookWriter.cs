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
        string family, IReadOnlyList<FieldRow> rows, int assetCount, string buildId, string readDate,
        IReadOnlyDictionary<string, Meaning>? meanings = null, int? limitedTo = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Fields — family `{family}`");
        sb.AppendLine();
        sb.AppendLine($"Read from **{assetCount}** assets. SCUM build **{buildId}** (read {readDate}).");
        if (limitedTo is int limit)
            sb.AppendLine($"> **Limited run:** only the first **{limit}** assets of this family were read — not a full notebook.");
        sb.AppendLine();
        sb.AppendLine($"Distinct fields: **{rows.Count}**.");
        sb.AppendLine();
        sb.AppendLine("`assets` = how many of the family's assets carry the field. `nested` = the field sits " +
                      "inside a struct or list (its name is a dotted path).");
        sb.AppendLine();
        sb.AppendLine("| field | type | assets | nested | example value | meaning |");
        sb.AppendLine("|---|---|---:|---|---|---|");
        foreach (var row in rows)
        {
            var nested = row.Nested ? "yes" : "";
            sb.AppendLine($"| `{row.Name}` | {Esc(row.Type)} | {row.Assets} | {nested} | {Esc(row.Example)} | {MeaningCell(row.Name, meanings)} |");
        }
        return sb.ToString();
    }

    private static string MeaningCell(string field, IReadOnlyDictionary<string, Meaning>? meanings)
    {
        if (meanings is not null && meanings.TryGetValue(field, out var meaning) && meaning.Text.Length > 0)
            return $"{Esc(meaning.Text)} ({Esc(meaning.Confidence)})";
        return "Assumed (not yet reviewed)";
    }

    internal static string IndexPage(IReadOnlyList<FamilySummary> families, string buildId, string readDate, int? limitedTo = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# The Game Model — what's in SCUM's data");
        sb.AppendLine();
        sb.AppendLine($"The facts below were read from the installed game: SCUM build **{buildId}** (read {readDate}).");
        if (limitedTo is int limit)
            sb.AppendLine($"> **Limited run:** only the first **{limit}** assets of each family were read — not a full notebook.");
        sb.AppendLine();
        sb.AppendLine("The field tables are written by the reading tool. Re-run it after a SCUM update and diff " +
                      "the notebook to see exactly what moved.");
        sb.AppendLine();
        sb.AppendLine("The family-to-family **links** are in [`links.md`](links.md).");
        sb.AppendLine();
        sb.AppendLine("| family | assets | fields |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var family in families)
            sb.AppendLine($"| [`{family.Family}`](fields/{family.Family}.md) | {family.Assets} | {family.Fields} |");
        return sb.ToString();
    }

    internal static string LinksPage(IReadOnlyList<FamilyLink> links, string buildId, string readDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Links between families");
        sb.AppendLine();
        sb.AppendLine($"Read from the installed game: SCUM build **{buildId}** (read {readDate}).");
        sb.AppendLine();
        sb.AppendLine("An edge means: assets in the *source* family hold a value that points at an asset in the " +
                      "*target* family. `refs` counts every reference seen; `assets` counts distinct source assets. " +
                      "Links come from the values the reader collected (a first cut).");
        sb.AppendLine();
        sb.AppendLine("| source family | → target family | refs | assets | example field | example value |");
        sb.AppendLine("|---|---|---:|---:|---|---|");
        foreach (var link in links)
            sb.AppendLine($"| `{link.From}` | `{link.To}` | {link.Refs} | {link.AssetCount} | `{Esc(link.ExampleField)}` | `{Esc(link.ExampleValue)}` |");
        return sb.ToString();
    }

    /// <summary>Keeps a value from breaking the Markdown table (pipes, newlines).</summary>
    private static string Esc(string value) => value        .Replace("\\", "\\\\")
        .Replace("|", "\\|")
        .Replace("\r", " ")
        .Replace("\n", " ");
}
