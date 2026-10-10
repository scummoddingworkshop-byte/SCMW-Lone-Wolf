using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SCMW.Backend.Build;

namespace SCMW.App;

/// <summary>What kind of backup this is.</summary>
public enum BackupKind { Mod, Project }

/// <summary>One backup on disk: a removed mod file, or a deleted project folder.</summary>
public sealed record BackupEntry(BackupKind Kind, string DisplayName, DateTime WhenUtc, string Path);

public interface IBackupsService
{
    bool IsAvailable { get; }

    /// <summary>The workspace's Backups folder, so it can be opened in Explorer.</summary>
    string Folder { get; }

    IReadOnlyList<BackupEntry> List();

    /// <summary>Puts a backup back (into SCUM's mods folder, or the workspace's Projects). Null on success.</summary>
    ModsResult Restore(BackupEntry entry);

    /// <summary>Deletes a backup for good.</summary>
    ModsResult Delete(BackupEntry entry);
}

/// <summary>
/// The safety net SCMW — Lone Wolf keeps in the workspace: removed mods live in <c>Backups\mods</c> and
/// deleted project folders in <c>Backups\deleted-projects</c>. This lists them, restores them, or
/// deletes them for good. It never touches the game's own files.
/// </summary>
public sealed class BackupsService : IBackupsService
{
    private const string ModsBackupsName = "mods";
    private const string ProjectsBackupsName = "deleted-projects";

    /// <summary>The trailing " (yyyyMMdd-HHmmss)" (and any " (n)" collision suffix) we add on backup.</summary>
    private static readonly Regex Stamp = new(@"\s\((\d{8}-\d{6})\)(\s\(\d+\))?$", RegexOptions.Compiled);

    private readonly string _backupsRoot;
    private readonly string _modsBackups;
    private readonly string _projectsBackups;
    private readonly string _projectsRoot;
    private readonly string _modsFolder;

    public BackupsService(string? gameRoot, string workspaceRoot)
    {
        _backupsRoot = Path.Combine(workspaceRoot, "Backups");
        _modsBackups = Path.Combine(_backupsRoot, ModsBackupsName);
        _projectsBackups = Path.Combine(_backupsRoot, ProjectsBackupsName);
        _projectsRoot = Path.Combine(workspaceRoot, "Projects");

        var root = (gameRoot ?? "").TrimEnd('\\', '/');
        _modsFolder = root.Length == 0 ? "" : BuildNames.ModsFolder(root);
    }

    public bool IsAvailable => Directory.Exists(_backupsRoot);

    public string Folder => _backupsRoot;

    public IReadOnlyList<BackupEntry> List()
    {
        var entries = new List<BackupEntry>();

        if (Directory.Exists(_modsBackups))
        {
            foreach (var file in Directory.GetFiles(_modsBackups))
            {
                var name = Path.GetFileName(file);
                var original = StripStamp(Path.GetFileNameWithoutExtension(name)) + Path.GetExtension(name);
                entries.Add(new BackupEntry(BackupKind.Mod, original, StampOf(name), file));
            }
        }

        if (Directory.Exists(_projectsBackups))
        {
            foreach (var dir in Directory.GetDirectories(_projectsBackups))
            {
                var name = Path.GetFileName(dir);
                entries.Add(new BackupEntry(BackupKind.Project, StripStamp(name), StampOf(name), dir));
            }
        }

        return entries.OrderByDescending(e => e.WhenUtc).ToList();
    }

    public ModsResult Restore(BackupEntry entry)
    {
        try
        {
            if (entry.Kind == BackupKind.Mod) return RestoreMod(entry);
            return RestoreProject(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var kind = WriteFailure.Classify(ex);
            return new ModsResult(false, WriteFailure.Message(kind, "restore that backup"), kind);
        }
    }

    private ModsResult RestoreMod(BackupEntry entry)
    {
        if (_modsFolder.Length == 0)
            return new ModsResult(false, "Set your SCUM folder in Settings first.");

        var name = Path.GetFileName(entry.Path);
        var original = StripStamp(Path.GetFileNameWithoutExtension(name)) + Path.GetExtension(name);
        var target = Path.Combine(_modsFolder, original);

        if (File.Exists(target) || File.Exists(target + ".disabled"))
            return new ModsResult(false, "A mod with that name is already in SCUM's mods folder.");

        Directory.CreateDirectory(_modsFolder);
        File.Copy(entry.Path, target, overwrite: false);
        return new ModsResult(true, null);
    }

    private ModsResult RestoreProject(BackupEntry entry)
    {
        var original = StripStamp(Path.GetFileName(entry.Path));
        var target = Path.Combine(_projectsRoot, original);

        if (Directory.Exists(target))
            return new ModsResult(false, "A project folder with that name is already there.");

        Directory.CreateDirectory(_projectsRoot);
        Directory.Move(entry.Path, target);     // the backup is consumed — the project is home again
        return new ModsResult(true, null);
    }

    public ModsResult Delete(BackupEntry entry)
    {
        try
        {
            if (Directory.Exists(entry.Path)) Directory.Delete(entry.Path, recursive: true);
            else if (File.Exists(entry.Path)) File.Delete(entry.Path);
            return new ModsResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ModsResult(false, "That backup could not be deleted.");
        }
    }

    private static string StripStamp(string name)
    {
        var match = Stamp.Match(name);
        return match.Success ? name[..match.Index] : name;
    }

    private static DateTime StampOf(string name)
    {
        var match = Stamp.Match(name);
        if (!match.Success) return DateTime.MinValue;

        return DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
            ? when
            : DateTime.MinValue;
    }
}
