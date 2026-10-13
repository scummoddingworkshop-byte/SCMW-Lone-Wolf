namespace SCMW.Core.Setup;

/// <summary>
/// Tolerant hand-written parser for the VDF subset Steam uses in <c>libraries.vdf</c>
/// and <c>appmanifest_*.acf</c>: quoted string pairs, nested braces, whitespace-insensitive,
/// CRLF-safe, with <c>\\</c> escape sequences in quoted values unescaped.
/// </summary>
public static class AcfParser
{
    /// <summary>First value found for <paramref name="key"/>, or null when absent.</summary>
    public static string? ReadValue(string acf, string key)
    {
        foreach (var pair in Pairs(acf))
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        return null;
    }

    /// <summary>All values found for <paramref name="key"/> in file order (e.g. one <c>path</c> per library folder).</summary>
    public static IReadOnlyList<string> ReadValues(string acf, string key) => Pairs(acf)
        .Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
        .Select(p => p.Value)
        .ToList();

    /// <summary>
    /// Extracts every key-value pair. VDF nests blocks under braces; keys whose value is
    /// a block (followed by <c>{</c>) are skipped, adjacent string tokens are paired
    /// key->value at any nesting depth, in file order.
    /// </summary>
    private static List<KeyValuePair<string, string>> Pairs(string acf)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        string? pendingKey = null;
        var i = 0;

        while (i < acf.Length)
        {
            var c = acf[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '{' || c == '}')
            {
                // A key immediately followed by a block brace names a nested table, not a scalar pair.
                pendingKey = null;
                i++;
            }
            else
            {
                var token = ReadToken(acf, ref i);
                if (token.Length == 0) continue;
                if (pendingKey is null)
                    pendingKey = token;
                else
                {
                    pairs.Add(new KeyValuePair<string, string>(pendingKey, token));
                    pendingKey = null;
                }
            }
        }

        return pairs;
    }

    /// <summary>Reads one token: a quoted string (with escapes) or a bare word.</summary>
    private static string ReadToken(string acf, ref int i)
    {
        if (acf[i] == '"')
        {
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < acf.Length && acf[i] != '"')
            {
                sb.Append(Unescape(acf, ref i));
                if (i >= acf.Length) break; // tolerate unterminated quote
            }
            if (i < acf.Length) i++; // closing quote
            return sb.ToString();
        }

        var start = i;
        while (i < acf.Length && !char.IsWhiteSpace(acf[i]) && acf[i] != '{' && acf[i] != '}' && acf[i] != '"')
            i++;
        return acf[start..i];
    }

    private static string Unescape(string acf, ref int i)
    {
        var c = acf[i];
        if (c != '\\' || i + 1 >= acf.Length)
        {
            i++;
            return c.ToString();
        }
        var next = acf[i + 1];
        i += 2;
        return next switch
        {
            '\\' => "\\",
            '"' => "\"",
            'n' => "\n",
            't' => "\t",
            _ => next.ToString(),
        };
    }
}
