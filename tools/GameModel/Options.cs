namespace SCMW.GameModel;

/// <summary>The tool's command line. Only <c>--out</c> is required.</summary>
/// <param name="OutDir">Folder to write the Markdown notebook into.</param>
/// <param name="GamePath">SCUM install folder; overrides the app's saved game path.</param>
/// <param name="AesKey">The pak key; overrides the app's saved key.</param>
/// <param name="Limit">Read only this many assets per family (a quick test run).</param>
internal sealed record Options(string OutDir, string? GamePath, string? AesKey, int? Limit)
{
    internal static bool TryParse(string[] args, out Options options, out string error)
    {
        options = new Options(string.Empty, null, null, null);
        error = string.Empty;

        string? outDir = null, game = null, key = null;
        int? limit = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    if (!Next(args, ref i, out outDir)) { error = "--out needs a folder."; return false; }
                    break;
                case "--game":
                    if (!Next(args, ref i, out game)) { error = "--game needs a folder."; return false; }
                    break;
                case "--key":
                    if (!Next(args, ref i, out key)) { error = "--key needs a value."; return false; }
                    break;
                case "--limit":
                    if (!Next(args, ref i, out var limitText)) { error = "--limit needs a number."; return false; }
                    if (!int.TryParse(limitText, out var n) || n <= 0)
                    {
                        error = "--limit needs a positive whole number.";
                        return false;
                    }
                    limit = n;
                    break;
                default:
                    error = $"Unknown option: {args[i]}";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(outDir))
        {
            error = "--out <folder> is required (where to write the notebook).";
            return false;
        }

        options = new Options(outDir!, game, key, limit);
        return true;
    }

    private static bool Next(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length) { value = string.Empty; return false; }
        value = args[++i];
        return true;
    }
}
