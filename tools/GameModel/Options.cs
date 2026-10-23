namespace SCMW.GameModel;

/// <summary>One make-mode edit: change <paramref name="Field"/> on <paramref name="AssetId"/> to <paramref name="Value"/>.</summary>
internal sealed record SetEdit(string AssetId, string Field, string Value);

/// <summary>
/// The tool's command line. Give one of four modes: <c>--out</c> (write the Markdown notebook),
/// <c>--dump</c> (print one asset's fields and write nothing), <c>--make</c> (write a small throwaway
/// test-mod .pak from a list of <c>--set</c> edits), or <c>--diff</c> (compare a mod's assets with the
/// game's and print the fields that differ).
/// </summary>
/// <param name="OutDir">Folder to write the Markdown notebook into (notebook mode).</param>
/// <param name="GamePath">SCUM install folder; overrides the app's saved game path.</param>
/// <param name="AesKey">The pak key; overrides the app's saved key.</param>
/// <param name="Limit">Read only this many assets per family (a quick test run).</param>
/// <param name="Dump">A word to find in an asset's path; prints that asset's fields (dump mode).</param>
/// <param name="Field">With <c>--dump</c>, print only fields whose name contains this word.</param>
/// <param name="Make">Output .pak path to write the changed assets into (make mode).</param>
/// <param name="Sets">The edits to apply in make mode; one per <c>--set</c>.</param>
/// <param name="Repak">Path to repak.exe; defaults to <c>tools\repak\repak.exe</c> under the working folder.</param>
/// <param name="Diff">A folder holding a mod's .pak file(s); print the fields that differ from the game.</param>
/// <param name="ClonePak">THROWAWAY probe: output .pak path to write a cloned asset into.</param>
/// <param name="From">THROWAWAY probe: the game asset id to clone (no .uasset).</param>
/// <param name="As">THROWAWAY probe: the new asset id to write the clone as (no .uasset).</param>
/// <param name="Registry">THROWAWAY probe: walk an AssetRegistry.bin file and report its structure.</param>
/// <param name="Drop">THROWAWAY probe: with --clone, remove this asset from the registry instead (a read test).</param>
/// <param name="Inspect">THROWAWAY probe: print a .uasset's names, imports and exports.</param>
/// <param name="RecipeGroups">Write every recipe's crafting category and DisplayPriority as JSON (kept).</param>
internal sealed record Options(string OutDir, string? GamePath, string? AesKey,
    int? Limit, string? Dump, string? Field, string? Make, IReadOnlyList<SetEdit> Sets, string? Repak,
    string? Diff, string? ClonePak, string? From, string? As, string? Registry, string? Drop, string? Inspect,
    string? RecipeGroups)
{
    internal static bool TryParse(string[] args, out Options options, out string error)
    {
        options = new Options(string.Empty, null, null, null, null, null, null, Array.Empty<SetEdit>(), null, null,
            null, null, null, null, null, null, null);
        error = string.Empty;

        string? outDir = null, game = null, key = null, dump = null, field = null, make = null, repak = null, diff = null;
        string? clone = null, from = null, asId = null, registry = null, drop = null, inspect = null;
        string? recipeGroups = null;
        int? limit = null;
        var sets = new List<SetEdit>();

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
                case "--dump":
                    if (!Next(args, ref i, out dump)) { error = "--dump needs a search word."; return false; }
                    break;
                case "--field":
                    if (!Next(args, ref i, out field)) { error = "--field needs a search word."; return false; }
                    break;
                case "--make":
                    if (!Next(args, ref i, out make)) { error = "--make needs an output .pak path."; return false; }
                    break;
                case "--repak":
                    if (!Next(args, ref i, out repak)) { error = "--repak needs a path to repak.exe."; return false; }
                    break;
                case "--diff":
                    if (!Next(args, ref i, out diff)) { error = "--diff needs a folder."; return false; }
                    break;
                case "--clone":
                    if (!Next(args, ref i, out clone)) { error = "--clone needs an output .pak path."; return false; }
                    break;
                case "--from":
                    if (!Next(args, ref i, out from)) { error = "--from needs an asset path."; return false; }
                    break;
                case "--as":
                    if (!Next(args, ref i, out asId)) { error = "--as needs a new asset path."; return false; }
                    break;
                case "--registry":
                    if (!Next(args, ref i, out registry)) { error = "--registry needs a file path."; return false; }
                    break;
                case "--drop":
                    if (!Next(args, ref i, out drop)) { error = "--drop needs an asset name."; return false; }
                    break;
                case "--inspect":
                    if (!Next(args, ref i, out inspect)) { error = "--inspect needs a .uasset path."; return false; }
                    break;
                case "--recipe-groups":
                    if (!Next(args, ref i, out recipeGroups)) { error = "--recipe-groups needs an output .json path."; return false; }
                    break;
                case "--set":
                    if (!Next(args, ref i, out var assetId)) { error = "--set needs an asset path."; return false; }
                    if (!Next(args, ref i, out var assignment)) { error = "--set needs field=value."; return false; }
                    var equals = assignment.IndexOf('=');
                    if (equals <= 0) { error = "--set expects field=value."; return false; }
                    sets.Add(new SetEdit(assetId, assignment[..equals], assignment[(equals + 1)..]));
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

        var modes = 0;
        if (!string.IsNullOrWhiteSpace(outDir)) modes++;
        if (!string.IsNullOrWhiteSpace(dump)) modes++;
        if (!string.IsNullOrWhiteSpace(make)) modes++;
        if (!string.IsNullOrWhiteSpace(diff)) modes++;
        var registryIsMode = !string.IsNullOrWhiteSpace(registry) && string.IsNullOrWhiteSpace(clone);
        if (!string.IsNullOrWhiteSpace(clone)) modes++;
        if (registryIsMode) modes++;
        if (!string.IsNullOrWhiteSpace(inspect)) modes++;
        if (!string.IsNullOrWhiteSpace(recipeGroups)) modes++;
        if (modes == 0)
        {
            error = "Give one of --out <folder>, --dump <word>, --make <pak>, --diff <folder>, --clone <pak>, --registry <file>, or --recipe-groups <file>.";
            return false;
        }
        if (modes > 1)
        {
            error = "Give only one of --out, --dump, --make, --diff, --clone, --registry, or --recipe-groups.";
            return false;
        }

        if (field is not null && string.IsNullOrWhiteSpace(dump))
        {
            error = "--field only makes sense with --dump.";
            return false;
        }

        if (sets.Count > 0 && string.IsNullOrWhiteSpace(make))
        {
            error = "--set only makes sense with --make.";
            return false;
        }

        if (make is not null && sets.Count == 0)
        {
            error = "--make needs at least one --set.";
            return false;
        }

        if (repak is not null && string.IsNullOrWhiteSpace(make))
        {
            error = "--repak only makes sense with --make.";
            return false;
        }

        if ((from is not null || asId is not null) && string.IsNullOrWhiteSpace(clone))
        {
            error = "--from and --as only make sense with --clone.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(clone) && string.IsNullOrWhiteSpace(drop)
            && (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(asId)))
        {
            error = "--clone needs both --from <asset> and --as <new asset> (or --drop <asset>).";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(drop) && string.IsNullOrWhiteSpace(registry))
        {
            error = "--drop needs --registry <file>.";
            return false;
        }

        options = new Options(outDir ?? string.Empty, game, key, limit, dump, field, make, sets, repak, diff,
            clone, from, asId, registry, drop, inspect, recipeGroups);
        return true;
    }

    private static bool Next(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length) { value = string.Empty; return false; }
        value = args[++i];
        return true;
    }
}
