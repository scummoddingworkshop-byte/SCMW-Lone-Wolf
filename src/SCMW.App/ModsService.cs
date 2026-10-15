using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using SCMW.Backend.Build;

namespace SCMW.App;

/// <summary>One mod on disk: its file name, a friendly name, whether it is switched on, whether
/// SCMW built it (SCMW badge) or it came from elsewhere (External), and whether SCMW may change it
/// (a mod in another tool's folder is listed but left alone).</summary>
public sealed record ModsEntry(string FileName, string DisplayName, bool IsOn, bool IsExternal,
                               bool Managed = true, string Location = "~mods");

/// <summary>Whether a change worked; one plain reason if not.</summary>
public sealed record ModsResult(bool Ok, string? Error, WriteFailureKind Failure = WriteFailureKind.None);

/// <summary>How an import went: how many were added, skipped for each reason.</summary>
public sealed record ImportOutcome(bool Ok, string? Error, int Imported, int Skipped, int SkippedNotPak = 0,
    WriteFailureKind Failure = WriteFailureKind.None);

/// <summary>A mod we imported (no project), so removal is exact.</summary>
internal sealed record ImportedMod(string FileName, string SourcePath, string InstalledPath, DateTime ImportedUtc);

/// <summary>
/// What packing the mods into a ZIP did: how many went in, and one plain line for each mod that was
/// left out and why. <see cref="Ok"/> false carries a plain reason instead.
/// </summary>
public sealed record PackOutcome(bool Ok, string? Error, int Packed, IReadOnlyList<string> LeftOut);

public interface IModsService
{
    bool IsAvailable { get; }
    IReadOnlyList<ModsEntry> List();
    ModsResult SetEnabled(string fileName, bool enabled);
    ModsResult SetAllEnabled(bool enabled);
    ModsResult Remove(string fileName);
    ImportOutcome Import(IReadOnlyList<string> sourcePaths);

    /// <summary>
    /// Packs every mod SCMW made — never one that came from elsewhere — into a ZIP at
    /// <paramref name="zipPath"/>, with a plain-text note inside saying what went in and what was
    /// left out, and why. A switched-off mod is included as it is, keeping ".disabled" on its name,
    /// so the pack restores exactly the state it was taken from. Reads only; changes nothing.
    /// </summary>
    PackOutcome PackOurMods(string zipPath);
}

/// <summary>
/// Everything the Mods screen does on disk. It only ever touches SCUM's <c>~mods</c> folder plus
/// the workspace's records and backups — never the game's own files.
/// </summary>
public sealed class ModsService : IModsService
{
    private const string OffSuffix = ".pak.disabled";
    private const string OnSuffix = ".pak";

    /// <summary>The plain-text note that travels inside a pack, so anyone opening it knows the score.</summary>
    internal const string NoteFileName = "README.txt";

    private readonly string _modsFolder;
    private readonly IReadOnlyList<string> _otherModsFolders;
    private readonly string _backupsRoot;
    private readonly string _importedPath;
    private readonly string _buildsRoot;

    public ModsService(string? gameRoot, string workspaceRoot)
    {
        var root = (gameRoot ?? "").TrimEnd('\\', '/');
        _modsFolder = root.Length == 0 ? "" : BuildNames.ModsFolder(root);

        // Other tools put mods beside ~mods (e.g. Vortex's "Mods", and UE's "LogicMods"). We LIST
        // them so nothing is hidden, but we never switch or remove them — that would stomp on
        // someone else's setup (design §15, "good neighbours").
        var paks = root.Length == 0 ? "" : Path.GetDirectoryName(_modsFolder)!;
        _otherModsFolders = paks.Length == 0
            ? Array.Empty<string>()
            : new[]
            {
                Path.Combine(paks, "Mods"),
                Path.Combine(paks, "LogicMods"),
            };

        _backupsRoot = Path.Combine(workspaceRoot, "Backups", "mods");
        _buildsRoot = Path.Combine(workspaceRoot, "Builds");
        _importedPath = Path.Combine(workspaceRoot, "mods", "imported.json");
    }

    /// <summary>True when a SCUM folder is configured (its Paks folder exists). ~mods may be empty.</summary>
    public bool IsAvailable =>
        _modsFolder.Length > 0 && Directory.Exists(Path.GetDirectoryName(_modsFolder));

    public IReadOnlyList<ModsEntry> List()
    {
        var ours = OurBaseNames();
        var entries = new List<ModsEntry>();
        Collect(entries, _modsFolder, "~mods", managed: true, ours);
        foreach (var folder in _otherModsFolders)
            Collect(entries, folder, Path.GetFileName(folder), managed: false, ours);
        return entries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Adds the mod files in one folder. A mod in another tool's folder is listed but not
    /// managed (never switched or removed by us).</summary>
    private static void Collect(List<ModsEntry> entries, string folder, string location, bool managed,
                                HashSet<string> ours)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.GetFiles(folder))
        {
            var name = Path.GetFileName(path);
            var split = Split(name);
            if (split is null) continue;
            var (isOn, display) = split.Value;
            entries.Add(new ModsEntry(name, display, isOn,
                IsExternal: !managed || !ours.Contains(display), Managed: managed, Location: location));
        }
    }

    private static (bool IsOn, string Display)? Split(string fileName)
    {
        if (fileName.EndsWith(OffSuffix, StringComparison.OrdinalIgnoreCase))
            return (false, fileName[..^OffSuffix.Length]);
        if (fileName.EndsWith(OnSuffix, StringComparison.OrdinalIgnoreCase))
            return (true, fileName[..^OnSuffix.Length]);
        return null;
    }

    /// <summary>A plain reason for a file we will not touch: it sits in another tool's folder.</summary>
    private string NotOursMessage(string fileName)
    {
        var elsewhere = _otherModsFolders.FirstOrDefault(f => File.Exists(Path.Combine(f, fileName)));
        return elsewhere is null
            ? "That file is not a SCUM mod."
            : $"\"{fileName}\" sits in SCUM's {Path.GetFileName(elsewhere)} folder, which another tool manages — SCMW — Lone Wolf leaves it alone.";
    }

    /// <summary>The display base-names of every mod we built (from Plan 13's per-project records),
    /// so a switched-off copy is still badged SCMW.</summary>
    private HashSet<string> OurBaseNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_buildsRoot)) return names;

        foreach (var dir in Directory.GetDirectories(_buildsRoot))
        {
            var file = Path.Combine(dir, "installed.json");
            if (!File.Exists(file)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(file));
                if (record is null) continue;
                var split = Split(Path.GetFileName(record.InstalledPath));
                if (split is not null) names.Add(split.Value.Display);
            }
            catch { /* a corrupt record just means "not ours" */ }
        }

        return names;
    }

    public ModsResult SetEnabled(string fileName, bool enabled)
    {
        var path = Path.Combine(_modsFolder, fileName);
        if (!File.Exists(path)) return new ModsResult(false, NotOursMessage(fileName));

        var isOff = fileName.EndsWith(OffSuffix, StringComparison.OrdinalIgnoreCase);
        var isOn = !isOff && fileName.EndsWith(OnSuffix, StringComparison.OrdinalIgnoreCase);
        if (!isOn && !isOff) return new ModsResult(false, "That file is not a SCUM mod.");

        try
        {
            if (enabled && isOff)
            {
                var target = path[..^".disabled".Length];
                if (File.Exists(target)) return new ModsResult(false, "A mod with that name is already switched on.");
                File.Move(path, target);
            }
            else if (!enabled && isOn)
            {
                var target = path + ".disabled";
                if (File.Exists(target)) return new ModsResult(false, "That mod is already switched off.");
                File.Move(path, target);
            }

            return new ModsResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var kind = WriteFailure.Classify(ex);
            return new ModsResult(false, WriteFailure.Message(kind, "switch that mod"), kind);
        }
    }

    public ModsResult SetAllEnabled(bool enabled)
    {
        foreach (var entry in List())
        {
            if (!entry.Managed || entry.IsOn == enabled) continue;
            var result = SetEnabled(entry.FileName, enabled);
            if (!result.Ok) return result;
        }

        return new ModsResult(true, null);
    }
    public ModsResult Remove(string fileName)
    {
        var path = Path.Combine(_modsFolder, fileName);
        if (!File.Exists(path))
        {
            // A mod listed from another tool's folder is never removed by us (design §15).
            return _otherModsFolders.Any(f => File.Exists(Path.Combine(f, fileName)))
                ? new ModsResult(false, NotOursMessage(fileName))
                : new ModsResult(true, null);   // already gone
        }

        try
        {
            Directory.CreateDirectory(_backupsRoot);
            var backup = Path.Combine(_backupsRoot,
                $"{Path.GetFileNameWithoutExtension(path)} ({DateTime.Now:yyyyMMdd-HHmmss}){Path.GetExtension(path)}");
            File.Copy(path, backup, overwrite: true);     // back up FIRST
            File.Delete(path);
            ForgetImported(fileName);
            ForgetBuiltRecord(fileName);
            return new ModsResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var kind = WriteFailure.Classify(ex);
            return new ModsResult(false, WriteFailure.Message(kind, "remove that mod"), kind);
        }
    }

    /// <summary>
    /// Packs the mods SCMW made into one ZIP, with a note inside saying what went in and what was
    /// left out. A mod added from a file, or one another tool keeps, is never passed on — it is
    /// listed in the note instead, with the reason.
    /// </summary>
    public PackOutcome PackOurMods(string zipPath)
    {
        var ours = new List<ModsEntry>();
        var leftOut = new List<string>();

        foreach (var entry in List())
        {
            if (entry.Managed && !entry.IsExternal) ours.Add(entry);
            else leftOut.Add(WhyLeftOut(entry));
        }

        if (ours.Count == 0)
            return new PackOutcome(false, "There are no SCMW mods to pack yet.", 0, leftOut);

        try
        {
            var folder = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            // Flat: the files sit at the top of the ZIP, so they can be dropped straight into ~mods.
            foreach (var mod in ours)
                zip.CreateEntryFromFile(Path.Combine(_modsFolder, mod.FileName!), mod.FileName!);

            WriteNote(zip, ours, leftOut);
            return new PackOutcome(true, null, ours.Count, leftOut);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var kind = WriteFailure.Classify(ex);
            return new PackOutcome(false, WriteFailure.Message(kind, "write that ZIP file"), 0, leftOut);
        }
    }

    /// <summary>One plain line for the note: what this mod is, and why it is not in the pack.</summary>
    private static string WhyLeftOut(ModsEntry entry) => !entry.Managed
        ? $"{entry.FileName} — kept by another tool ({entry.Location}), so SCMW does not pass it on"
        : $"{entry.FileName} — added from a file, not made with SCMW — Lone Wolf";

    /// <summary>The note that travels inside the pack: what is in it, how to use it, and what is not.</summary>
    private static void WriteNote(ZipArchive zip, IReadOnlyList<ModsEntry> ours, IReadOnlyList<string> leftOut)
    {
        using var note = new StreamWriter(zip.CreateEntry(NoteFileName).Open());

        note.WriteLine("SCMW — Lone Wolf mod pack");
        note.WriteLine($"Packed {DateTime.Now:yyyy-MM-dd HH:mm}");
        note.WriteLine();

        note.WriteLine($"In this pack — {ours.Count} {(ours.Count == 1 ? "mod" : "mods")} made with SCMW — Lone Wolf:");
        foreach (var mod in ours)
            note.WriteLine($"  {mod.FileName}{(mod.IsOn ? "" : "   (switched off)")}");

        note.WriteLine();
        note.WriteLine("How to use it");
        note.WriteLine("-------------");
        note.WriteLine("Copy these files into SCUM's mods folder:");
        note.WriteLine(@"  <SCUM folder>\SCUM\Content\Paks\~mods");
        note.WriteLine("Then switch on the mods you want, in SCMW — Lone Wolf's Mods Management");
        note.WriteLine("screen. A name ending in \".disabled\" is a mod that was switched off when");
        note.WriteLine("this pack was made — switch it on there, or drop \".disabled\" from its name.");
        note.WriteLine();

        if (leftOut.Count == 0)
        {
            note.WriteLine("Nothing was left out: every mod here was made with SCMW — Lone Wolf.");
            return;
        }

        note.WriteLine("Left out — not made with SCMW — Lone Wolf, so not ours to pass on:");
        foreach (var line in leftOut) note.WriteLine($"  {line}");
    }

    private void ForgetImported(string fileName)
    {
        try
        {
            var list = ReadImported();
            if (list.RemoveAll(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)) > 0)
                WriteImported(list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the note is harmless: the file is gone and the folder scan is the truth.
        }
    }

    /// <summary>
    /// Clears a built mod's install record when its file is removed, so Build stops claiming it is
    /// installed (and cannot resurrect it from an old backup).
    /// </summary>
    private void ForgetBuiltRecord(string fileName)
    {
        if (!Directory.Exists(_buildsRoot)) return;
        var split = Split(fileName);
        if (split is null) return;
        var wanted = split.Value.Display;

        foreach (var dir in Directory.GetDirectories(_buildsRoot))
        {
            var file = Path.Combine(dir, "installed.json");
            if (!File.Exists(file)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(file));
                if (record is null) continue;
                var recordSplit = Split(Path.GetFileName(record.InstalledPath));
                if (recordSplit is not null &&
                    string.Equals(recordSplit.Value.Display, wanted, StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
            catch { /* a corrupt/unreadable record is left alone */ }
        }
    }

    internal List<ImportedMod> ReadImported()
    {
        if (!File.Exists(_importedPath)) return new List<ImportedMod>();
        try { return JsonSerializer.Deserialize<List<ImportedMod>>(File.ReadAllText(_importedPath)) ?? new(); }
        catch { return new List<ImportedMod>(); }
    }

    internal void WriteImported(List<ImportedMod> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_importedPath)!);
        File.WriteAllText(_importedPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void TryWriteImported(List<ImportedMod> list)
    {
        try { WriteImported(list); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The mod is still copied in and will list from the folder scan; only the note is lost.
        }
    }

    public ImportOutcome Import(IReadOnlyList<string> sourcePaths)
    {
        if (!IsAvailable) return new ImportOutcome(false, "Set your SCUM folder in Settings first.", 0, 0);
        Directory.CreateDirectory(_modsFolder);

        var imported = 0;
        var skipped = 0;
        var notPak = 0;
        var records = ReadImported();

        foreach (var source in sourcePaths)
        {
            var name = Path.GetFileName(source);
            if (!name.EndsWith(OnSuffix, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(OffSuffix, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                notPak++;
                continue;
            }

            var dest = Path.Combine(_modsFolder, name);
            if (File.Exists(dest) || File.Exists(dest + ".disabled"))   // already installed (on or off)
            {
                skipped++;
                continue;
            }

            var temp = dest + "." + Guid.NewGuid().ToString("N") + ".adding";
            try
            {
                // Copy to a temp name, then move into place — a failure mid-copy never leaves a
                // half-copied .pak that the game would try to load.
                File.Copy(source, temp, overwrite: false);
                File.Move(temp, dest);
                records.Add(new ImportedMod(name, source, dest, DateTime.UtcNow));
                imported++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (File.Exists(temp)) { try { File.Delete(temp); } catch { /* best effort */ } }
                if (imported > 0) TryWriteImported(records);
                var kind = WriteFailure.Classify(ex);
                return new ImportOutcome(false, WriteFailure.Message(kind, "add that mod"), imported, skipped,
                    Failure: kind);
            }
        }

        if (imported > 0) TryWriteImported(records);
        // The view model builds the plain message from the counts; Error is only a fallback.
        return new ImportOutcome(imported > 0, null, imported, skipped, notPak);
    }
}
