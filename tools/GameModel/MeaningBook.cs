using System.Text;

namespace SCMW.GameModel;

/// <summary>One field's authored meaning and how far we trust it.</summary>
internal sealed record Meaning(string Text, string Confidence);

/// <summary>
/// Parses the human-owned <c>meanings.md</c>: one <c>## family</c> section per family,
/// each with a <c>| field | meaning | confidence |</c> table. Read-only here — the
/// reader never writes this file. A field with no entry simply has no meaning yet.
/// </summary>
internal static class MeaningBook
{
    internal static IReadOnlyDictionary<(string Family, string Field), Meaning> Parse(string text)
    {
        var book = new Dictionary<(string, string), Meaning>();
        var family = string.Empty;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                family = line[3..].Trim().Trim('`');
                continue;
            }

            if (family.Length == 0 || !line.StartsWith('|')) continue;

            var cells = SplitCells(line);
            if (cells.Length < 3) continue;

            var field = cells[0].Trim().Trim('`');
            var meaning = cells[1].Trim();
            var confidence = cells[2].Trim();

            if (field.Length == 0 || field == "field") continue;                          // header
            if (field.All(c => c is '-' or ':' or ' ')) continue;                          // separator
            if (meaning.Length == 0) continue;

            book[(family, field)] = new Meaning(meaning, confidence.Length == 0 ? "Assumed" : confidence);
        }

        return book;
    }

    /// <summary>Splits a table row on unescaped '|' and turns "\|" back into a plain "|".</summary>
    private static string[] SplitCells(string line)
    {
        var text = line.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|')) text = text[..^1];

        var cells = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (text[i] == '|')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(text[i]);
            }
        }

        cells.Add(current.ToString());
        return cells.ToArray();
    }
}
