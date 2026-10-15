using System.Globalization;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Core.Logging;
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
            // Details go to the log (any key is hidden there); the console stays plain.
            Log.Warn("GameModel: the reader failed (" + ex.GetType().Name + ").");
            Console.Error.WriteLine("The reader failed. See the log for details.");
            return 1;
        }
    }

    private static int Run(Options options)
    {
        // 1. What we know about the game: where it is, its key, and its build.
        var settings = new JsonSettingsStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCMW"))
            .Load();

        var located = ScumLocator.Locate(SteamSources.Default);

        var gamePath = options.GamePath
            ?? (located.Found ? located.Path : null)
            ?? (string.IsNullOrWhiteSpace(settings.GamePath) ? null : settings.GamePath);

        var aesKey = options.AesKey ?? settings.AesKey;

        // Only trust a build id we can tie to the folder we are actually reading.
        var buildId =
            located.Found && OutputGuard.SamePath(gamePath, located.Path) && !string.IsNullOrEmpty(located.BuildId) ? located.BuildId
            : OutputGuard.SamePath(gamePath, settings.GamePath) && !string.IsNullOrWhiteSpace(settings.BuildId) ? settings.BuildId!
            : "unknown";

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

        // Never write into a folder that isn't ours. Checked before the long read.
        var guardError = OutputGuard.Check(options.OutDir, options.Limit is not null);
        if (guardError is not null)
        {
            Console.Error.WriteLine(guardError);
            return 4;
        }

        Log.RegisterSecret(aesKey);

        Console.WriteLine($"Game:  {gamePath}");
        Console.WriteLine($"Build: {buildId}");
        Console.WriteLine($"Paks:  {paks}");
        Console.WriteLine();

        // Our own meanings (meanings.md), if we have written any yet. Read-only here.
        var meaningsPath = Path.Combine(options.OutDir, "meanings.md");
        var meaningBook = File.Exists(meaningsPath)
            ? MeaningBook.Parse(File.ReadAllText(meaningsPath))
            : new Dictionary<(string Family, string Field), Meaning>();
        Console.WriteLine(File.Exists(meaningsPath)
            ? $"Meanings: {meaningBook.Count} read from meanings.md."
            : "Meanings: none yet — every field will show 'Assumed (not yet reviewed)'.");

        // 2. Mount the game through the app's engine.
        using var mount = new PakMount();
        var source = new PakContentSource(mount);
        source.Configure(paks, aesKey, buildId);
        if (source.Status.State != ContentSourceState.Ready)
        {
            Console.Error.WriteLine("Could not open the game's files: " + source.Status.Message);
            return 3;
        }

        // 3. Walk the families and read every field of every asset. Nothing is written
        // until the whole read finishes, so a failure can never leave half a notebook.
        var allPaths = mount.FilePaths.ToArray();
        Console.WriteLine($"{allPaths.Length:N0} files in the paks.");

        var readDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pages = new List<(string Family, string Markdown)>();
        var summaries = new List<FamilySummary>();
        var links = new LinkCollector();
        var read = 0;
        var failed = 0;

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
                try
                {
                    var fields = source.FieldsAsync(assetId).GetAwaiter().GetResult();

                    // Links come from the game's own fields — not the synthetic string-table
                    // rows, whose summary text must never be mistaken for a reference.
                    links.Add(family, assetId, fields);

                    if (family == "string_tables")
                        fields = fields.Concat(ReadStringTable(mount, assetId)).ToArray();

                    perAsset.Add(fields);
                    read++;
                }
                catch (Exception ex)
                {
                    // One odd asset must not stop the run.
                    failed++;
                    Log.Warn($"GameModel: could not read '{assetId}' ({ex.GetType().Name}).");
                }

                if ((i + 1) % 250 == 0)
                    Console.WriteLine($"  {i + 1:N0}/{paths.Length:N0}");
            }

            var rows = FieldCollector.Collect(perAsset);
            var forFamily = meaningBook
                .Where(pair => pair.Key.Family == family)
                .ToDictionary(pair => pair.Key.Field, pair => pair.Value);

            pages.Add((family, NotebookWriter.FamilyPage(
                family, rows, paths.Length, buildId, readDate, forFamily, options.Limit)));
            summaries.Add(new FamilySummary(family, paths.Length, rows.Count));
            Console.WriteLine($"  -> {rows.Count} distinct fields.");
        }

        // 4. Write everything now the read is complete.
        var fieldsDir = Path.Combine(options.OutDir, "fields");
        Directory.CreateDirectory(fieldsDir);
        foreach (var (family, markdown) in pages)
            File.WriteAllText(Path.Combine(fieldsDir, family + ".md"), markdown);

        File.WriteAllText(
            Path.Combine(options.OutDir, "index.md"),
            NotebookWriter.IndexPage(summaries, buildId, readDate, options.Limit));

        File.WriteAllText(
            Path.Combine(options.OutDir, "links.md"),
            NotebookWriter.LinksPage(links.Rows(), buildId, readDate));

        Console.WriteLine();
        Console.WriteLine($"Read {read:N0} assets" + (failed > 0 ? $"; skipped {failed:N0} that could not be read (see the log)." : "."));
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
