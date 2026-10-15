using System.Globalization;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Core.Settings;
using SCMW.Core.Setup;

namespace SCMW.GameModel;

/// <summary>
/// The Game Model reader. Opens the installed SCUM through the app's own pak engine,
/// walks the seven gameplay families, and writes a Markdown notebook of every field.
/// Re-run after a SCUM update and diff the notebook to see what moved.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!Options.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine("Game Model reader — " + error);
            Console.Error.WriteLine("Usage: SCMW.GameModel --out <folder> [--game <folder>] [--key <aes>] [--limit <n>]");
            return 2;
        }

        try
        {
            return Run(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("The reader failed: " + ex.Message);
            return 1;
        }
    }

    private static int Run(Options options)
    {
        // 1. What we know about the game: where it is, its key, and its build. Prefer what
        // the app saved (so a normal run needs no arguments), then the game itself.
        var settings = new JsonSettingsStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCMW"))
            .Load();

        var located = ScumLocator.Locate(SteamSources.Default);

        var gamePath = options.GamePath
            ?? (located.Found ? located.Path : null)
            ?? (string.IsNullOrWhiteSpace(settings.GamePath) ? null : settings.GamePath);

        var aesKey = options.AesKey ?? settings.AesKey;

        var buildId = located.Found && !string.IsNullOrEmpty(located.BuildId)
            ? located.BuildId
            : (string.IsNullOrWhiteSpace(settings.BuildId) ? "unknown" : settings.BuildId);

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            Console.Error.WriteLine("SCUM install not found. Open the app once (so it saves the game path), or pass --game <folder>.");
            return 3;
        }

        var paks = Path.Combine(gamePath, "SCUM", "Content", "Paks");
        if (!Directory.Exists(paks))
        {
            Console.Error.WriteLine($"No 'Content\\Paks' folder under {gamePath}.");
            return 3;
        }

        if (string.IsNullOrWhiteSpace(aesKey))
        {
            Console.Error.WriteLine("No AES key. Open the app once (so it saves the key), or pass --key <aes>.");
            return 3;
        }

        Console.WriteLine($"Game:  {gamePath}");
        Console.WriteLine($"Build: {buildId}");
        Console.WriteLine($"Paks:  {paks}");
        Console.WriteLine();

        // 2. Mount the game through the app's engine.
        using var mount = new PakMount();
        var source = new PakContentSource(mount);
        source.Configure(paks, aesKey, buildId);
        if (source.Status.State != ContentSourceState.Ready)
        {
            Console.Error.WriteLine("Could not open the game's files: " + source.Status.Message);
            return 3;
        }

        // 3. Walk the families and read every field of every asset.
        var allPaths = mount.FilePaths.ToArray();
        Console.WriteLine($"{allPaths.Length:N0} files in the paks.");

        var readDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fieldsDir = Path.Combine(options.OutDir, "fields");
        Directory.CreateDirectory(fieldsDir);

        var summaries = new List<FamilySummary>();
        var links = new LinkCollector();
        foreach (var family in FamilyRules.Families)
        {
            var paths = allPaths
                .Where(path => FamilyRules.FamilyOf(path) == family)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (options.Limit is int limit && paths.Length > limit)
                paths = paths.Take(limit).ToArray();

            Console.WriteLine($"{family}: reading {paths.Length:N0} assets...");
            var perAsset = new List<IReadOnlyList<AssetField>>(paths.Length);
            for (var i = 0; i < paths.Length; i++)
            {
                var assetId = paths[i][..^".uasset".Length];
                var fields = source.FieldsAsync(assetId).GetAwaiter().GetResult();
                if (family == "string_tables")
                    fields = fields.Concat(ReadStringTable(mount, assetId)).ToArray();
                perAsset.Add(fields);
                links.Add(family, assetId, fields);
                if ((i + 1) % 250 == 0)
                    Console.WriteLine($"  {i + 1:N0}/{paths.Length:N0}");
            }

            var rows = FieldCollector.Collect(perAsset);
            File.WriteAllText(
                Path.Combine(fieldsDir, family + ".md"),
                NotebookWriter.FamilyPage(family, rows, paths.Length, buildId, readDate));

            summaries.Add(new FamilySummary(family, paths.Length, rows.Count));
            Console.WriteLine($"  -> {rows.Count} distinct fields.");
        }

        // 4. The index that ties the family pages together.
        File.WriteAllText(
            Path.Combine(options.OutDir, "index.md"),
            NotebookWriter.IndexPage(summaries, buildId, readDate));

        File.WriteAllText(
            Path.Combine(options.OutDir, "links.md"),
            NotebookWriter.LinksPage(links.Rows(), buildId, readDate));

        Console.WriteLine();
        Console.WriteLine($"Notebook written to {options.OutDir}");
        return 0;
    }

    /// <summary>The two members of a string table, which the app's reader does not see.</summary>
    private static IReadOnlyList<AssetField> ReadStringTable(PakMount mount, string assetId)
    {
        if (!mount.TryLoadExports(assetId, out var exports))
            return Array.Empty<AssetField>();

        var table = exports.OfType<UStringTable>().FirstOrDefault()?.StringTable;
        return table is null
            ? Array.Empty<AssetField>()
            : StringTableFields.Build(table.TableNamespace, table.KeysToEntries);
    }
}
