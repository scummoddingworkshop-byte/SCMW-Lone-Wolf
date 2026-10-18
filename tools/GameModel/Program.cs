using System.Globalization;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Objects.Core.i18N;
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
            Console.Error.WriteLine("       SCMW.GameModel --dump <word> [--field <word>] [--game <folder>] [--key <aes>]");
            Console.Error.WriteLine("       SCMW.GameModel --make <out.pak> --set <asset> <field>=<value> [--set ...] [--game <folder>] [--key <aes>] [--repak <path>]");
            Console.Error.WriteLine("       SCMW.GameModel --clone <out.pak> --from <asset> --as <new asset> [--game <folder>] [--key <aes>] [--repak <path>]");
            Console.Error.WriteLine("       SCMW.GameModel --registry <AssetRegistry.bin>");
            Console.Error.WriteLine("       SCMW.GameModel --inspect <file.uasset>");
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

        Log.RegisterSecret(aesKey);

        Console.WriteLine($"Game:  {gamePath}");
        Console.WriteLine($"Build: {buildId}");
        Console.WriteLine($"Paks:  {paks}");
        Console.WriteLine();

        // Registry probe (THROWAWAY): walk an AssetRegistry.bin file and report its structure.
        // When --clone is also given, the registry is an *input* to the clone instead.
        if (string.IsNullOrWhiteSpace(options.ClonePak) && !string.IsNullOrWhiteSpace(options.Registry))
            return RegistryProbe.Run(options.Registry!);

        // Inspect probe (THROWAWAY): print a .uasset's names, imports and exports.
        if (!string.IsNullOrWhiteSpace(options.Inspect))
            return AssetInspector.Run(options.Inspect!);

        // 2. Mount the game through the app's engine.
        using var mount = new PakMount();
        var source = new PakContentSource(mount);
        source.Configure(paks, aesKey, buildId);
        if (source.Status.State != ContentSourceState.Ready)
        {
            Console.Error.WriteLine("Could not open the game's files: " + source.Status.Message);
            return 3;
        }

        // Dump mode: print one asset's fields and stop. Writes nothing.
        if (!string.IsNullOrWhiteSpace(options.Dump))
            return Dump(options.Dump!, options.Field, mount, source);

        // Make mode: write a throwaway change-test pak and stop. Writes only the .pak asked for.
        if (!string.IsNullOrWhiteSpace(options.Make))
            return Make(options, mount);

        // Clone mode (THROWAWAY probe): write a pak holding a renamed copy of one asset.
        if (!string.IsNullOrWhiteSpace(options.ClonePak))
            return Clone(options, mount);

        // Diff mode: print the fields a mod changes against the game. Writes nothing.
        if (!string.IsNullOrWhiteSpace(options.Diff))
            return Diff(options.Diff!, mount, source, aesKey);

        // Never write into a folder that isn't ours. Checked before the long read.
        var guardError = OutputGuard.Check(options.OutDir, options.Limit is not null);
        if (guardError is not null)
        {
            Console.Error.WriteLine(guardError);
            return 4;
        }

        // Our own meanings (meanings.md), if we have written any yet. Read-only here.
        var meaningsPath = Path.Combine(options.OutDir, "meanings.md");
        var meaningBook = File.Exists(meaningsPath)
            ? MeaningBook.Parse(File.ReadAllText(meaningsPath))
            : new Dictionary<(string Family, string Field), Meaning>();
        Console.WriteLine(File.Exists(meaningsPath)
            ? $"Meanings: {meaningBook.Count} read from meanings.md."
            : "Meanings: none yet — every field will show 'Assumed (not yet reviewed)'.");

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

    /// <summary>
    /// Make mode: write a small throwaway change-test mod. Resolves the bundled repak location and
    /// hands the edits to <see cref="ModMaker"/>. Writes only the one .pak; installs nothing.
    /// </summary>
    private static int Make(Options options, PakMount mount)
    {
        var repak = options.Repak
            ?? Path.Combine(Directory.GetCurrentDirectory(), "tools", "repak", "repak.exe");
        if (!File.Exists(repak))
        {
            Console.Error.WriteLine($"repak.exe not found at '{repak}'. Pass --repak <path>.");
            return 3;
        }

        var outPak = Path.GetFullPath(options.Make!);
        var folder = Path.GetDirectoryName(outPak);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        return ModMaker.Run(outPak, options.Sets, mount, repak);
    }

    /// <summary>
    /// Clone mode (THROWAWAY probe): write a pak holding a renamed copy of one game asset, at a new
    /// path the game does not have. Resolves the bundled repak and hands off to
    /// <see cref="ProbeCloner"/>. Writes only the one .pak; installs nothing.
    /// </summary>
    private static int Clone(Options options, PakMount mount)
    {
        var repak = options.Repak
            ?? Path.Combine(Directory.GetCurrentDirectory(), "tools", "repak", "repak.exe");
        if (!File.Exists(repak))
        {
            Console.Error.WriteLine($"repak.exe not found at '{repak}'. Pass --repak <path>.");
            return 3;
        }

        var outPak = Path.GetFullPath(options.ClonePak!);
        var folder = Path.GetDirectoryName(outPak);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        if (!string.IsNullOrWhiteSpace(options.Drop))
            return ProbeCloner.RunRegistryDrop(outPak, options.Registry!, options.Drop!, repak);

        return ProbeCloner.Run(outPak, options.From!, options.As!, mount, repak, options.Registry);
    }

    /// <summary>
    /// Diff mode: for every asset the mod replaces, read it from the game and from the mod and print
    /// the fields whose value differs (or that the mod adds). A throwaway aid for the meaning work —
    /// it writes nothing.
    /// </summary>
    private static int Diff(string folder, PakMount gameMount, PakContentSource gameSource, string aesKey)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"'{folder}' is not a folder.");
            return 3;
        }

        using var modMount = new PakMount();
        if (!modMount.TryMount(folder, aesKey, out var mountError))
        {
            Console.Error.WriteLine("Could not open the mod's files: " + mountError);
            return 3;
        }

        var modSource = new PakContentSource(modMount);
        modSource.Configure(folder, aesKey, "mod");
        if (modSource.Status.State != ContentSourceState.Ready)
        {
            Console.Error.WriteLine("Could not read the mod's files: " + modSource.Status.Message);
            return 3;
        }

        var gamePaths = new HashSet<string>(gameMount.FilePaths, StringComparer.OrdinalIgnoreCase);
        var modAssets = modMount.FilePaths
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var processed = 0;
        var changedAssets = 0;

        // The two mounts print asset paths differently ("SCUM/Content/..." vs "/Game/..."), so a
        // reference would look "changed" when it is the same asset. Normalise before comparing.
        static string Norm(string v) => v.Replace("SCUM/Content/", "/").Replace("/Game/", "/");

        foreach (var path in modAssets)
        {
            if (!gamePaths.Contains(path)) continue;
            var assetId = path[..^".uasset".Length];
            processed++;

            IReadOnlyList<AssetField> gameFields, modFields;
            try
            {
                gameFields = gameSource.FieldsAsync(assetId).GetAwaiter().GetResult();
                modFields = modSource.FieldsAsync(assetId).GetAwaiter().GetResult();
            }
            catch { continue; }

            var game = gameFields.ToDictionary(f => f.TechnicalName, f => f.CurrentValue ?? "", StringComparer.Ordinal);
            var diffs = new List<string>();
            foreach (var f in modFields)
            {
                var modValue = f.CurrentValue ?? "";
                var gameValue = game.TryGetValue(f.TechnicalName, out var gv) ? gv : "";
                if (!string.Equals(Norm(gameValue), Norm(modValue), StringComparison.Ordinal))
                    diffs.Add($"    {f.TechnicalName}: '{gameValue}' -> '{modValue}'");
            }

            if (diffs.Count == 0) continue;
            changedAssets++;
            Console.WriteLine("== " + assetId);
            foreach (var d in diffs) Console.WriteLine(d);
            Console.WriteLine();
        }

        Console.WriteLine($"Read {processed:N0} replaced asset(s); {changedAssets:N0} showed a field difference.");
        return 0;
    }

    /// <summary>
    /// Dump mode: find assets whose path contains <paramref name="text"/> and print their fields
    /// (optionally only those whose name contains <paramref name="fieldFilter"/>). A throwaway aid
    /// for the in-game meaning checks — it writes nothing.
    /// </summary>
    private static int Dump(string text, string? fieldFilter, PakMount mount, PakContentSource source)
    {
        var matches = mount.FilePaths
            .Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            Console.WriteLine($"No asset path contains '{text}'.");
            return 0;
        }

        Console.WriteLine($"{matches.Length:N0} asset(s) match '{text}'" +
                          (fieldFilter is null ? "." : $"; showing fields containing '{fieldFilter}'."));
        Console.WriteLine();

        const int maxAssets = 10;
        foreach (var path in matches.Take(maxAssets))
        {
            var assetId = path[..^".uasset".Length];
            var name = assetId[(assetId.LastIndexOf('/') + 1)..];
            Console.WriteLine($"== {name} ==");
            Console.WriteLine($"   {assetId}");

            var rawByName = RawValues(mount, assetId);
            try
            {
                var fields = source.FieldsAsync(assetId).GetAwaiter().GetResult()
                    .Where(f => fieldFilter is null ||
                                f.TechnicalName.Contains(fieldFilter, StringComparison.OrdinalIgnoreCase) ||
                                f.FriendlyName.Contains(fieldFilter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.TechnicalName, StringComparer.Ordinal);

                foreach (var field in fields)
                {
                    var type = string.IsNullOrEmpty(field.ValueTypeHint) ? string.Empty : $"  [{field.ValueTypeHint}]";
                    var nested = field.Nested ? " (nested)" : string.Empty;
                    var value = string.IsNullOrEmpty(field.CurrentValue) ? string.Empty : $" = {field.CurrentValue}";
                    Console.WriteLine($"   {field.TechnicalName}{nested}{value}{type}");

                    var detail = TextDetail(rawByName.TryGetValue(field.TechnicalName, out var raw) ? raw : null);
                    if (detail.Length > 0)
                        Console.WriteLine(detail);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   could not read ({ex.GetType().Name})");
            }

            Console.WriteLine();
        }

        if (matches.Length > maxAssets)
            Console.WriteLine($"(showed {maxAssets} of {matches.Length:N0}; narrow the word to see fewer.)");

        return 0;
    }

    /// <summary>Every top-level property of an asset, by name: the class default object first,
    /// then the package's own exports. Enough to look up a field's raw value.</summary>
    private static Dictionary<string, object?> RawValues(PakMount mount, string assetId)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (mount.TryLoadPackageObject(CdoPathOf(assetId), out var cdo) && cdo is not null)
            foreach (var prop in cdo.Properties)
                map[string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text] = prop.Tag?.GenericValue;

        if (mount.TryLoadExports(assetId, out var exports))
            foreach (var export in exports)
                foreach (var prop in export.Properties)
                    map.TryAdd(string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text,
                        prop.Tag?.GenericValue);

        return map;
    }

    /// <summary>The dotted path of the class default object (".../Name.Default__Name_C").</summary>
    private static string CdoPathOf(string packagePath)
    {
        var path = packagePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            ? packagePath[..^7]
            : packagePath;
        var assetName = path[(path.LastIndexOf('/') + 1)..];
        return $"{path}.Default__{assetName}_C";
    }

    /// <summary>For a text field, the stored entry behind the resolved words — its kind, table
    /// and key — or empty for anything that is not text.</summary>
    private static string TextDetail(object? raw)
    {
        if (raw is not FText text)
            return string.Empty;

        return text.TextHistory switch
        {
            FTextHistory.Base b =>
                $"      text: Base          ns=\"{b.Namespace}\"  key=\"{b.Key}\"  source=\"{b.SourceString}\"",
            FTextHistory.StringTableEntry s =>
                $"      text: StringTable   table=\"{s.TableId.Text}\"  key=\"{s.Key}\"  source=\"{s.SourceString}\"",
            _ => $"      text: {text.HistoryType}"
        };
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
