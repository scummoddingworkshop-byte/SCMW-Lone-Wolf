namespace SCMW.Backend.Build;

/// <summary>
/// Places our built `.pak` into SCUM's `~mods` folder. It removes only other versions of the
/// SAME project, backs up (and can restore) any file whose name we would overwrite, and deletes
/// exactly the recorded file on removal. One plain reason on failure; nothing half-done.
/// </summary>
public sealed class ModInstaller
{
    private readonly IInstallStore _store;
    private readonly string _backupsRoot;

    public ModInstaller(IInstallStore store, string backupsRoot)
    {
        _store = store;
        _backupsRoot = backupsRoot;
    }

    public InstallOutcome Install(string gamePath, InstallRecord record, string sourcePakPath)
    {
        var mods = BuildNames.ModsFolder(gamePath);
        var paks = Path.GetDirectoryName(mods)!;
        if (!Directory.Exists(paks))
            return new InstallOutcome(false, null,
                "The SCUM folder could not be found. Check it in Settings, then try again.");

        try
        {
            Directory.CreateDirectory(mods);
            var target = Path.Combine(mods, record.PakFileName);
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".installing";

            // 1. Copy the new pak in under a temp name FIRST, so any failure changes nothing.
            File.Copy(sourcePakPath, temp, overwrite: true);

            string? backup = null;
            try
            {
                // 2. Back up a same-name file we are about to replace — but only a file that is NOT
                //    ours. A previous build of the same project has the same name, and backing that
                //    up would let "Remove mod" restore the mod it just deleted.
                if (File.Exists(target) && !BuildNames.IsOurPak(Path.GetFileName(target)))
                {
                    Directory.CreateDirectory(_backupsRoot);
                    backup = Path.Combine(_backupsRoot,
                        $"{Path.GetFileNameWithoutExtension(target)} ({DateTime.Now:yyyyMMdd-HHmmss}).pak");
                    File.Copy(target, backup, true);
                }

                // 3. Move the new pak into place before removing anything.
                File.Move(temp, target, overwrite: true);
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                throw;
            }

            // From here the pak IS in place, so nothing below may report the install as failed —
            // these are tidy-ups, and a hiccup is logged as a warning instead.
            try
            {
                // 4. Remove a switched-off same-name copy (the Mods screen renames files to disable
                //    them): it is the same mod, so keep only the switched-on one.
                var disabledTwin = target + ".disabled";
                if (File.Exists(disabledTwin))
                {
                    Directory.CreateDirectory(_backupsRoot);
                    File.Copy(disabledTwin, Path.Combine(_backupsRoot,
                        $"{Path.GetFileName(target)} ({DateTime.Now:yyyyMMdd-HHmmss}).disabled"), true);
                    File.Delete(disabledTwin);
                }

                // 5. Remove older versions of THIS project only (never the file we just wrote).
                foreach (var file in Directory.GetFiles(mods))
                {
                    if (PathsEqual(file, target)) continue;
                    if (BuildNames.IsOlderPakOfProject(Path.GetFileName(file), record.ProjectName))
                        File.Delete(file);
                }

                // 6. Remove a previous install recorded under a different name (a project rename).
                var previous = _store.Load(record.ProjectId);
                if (previous is not null && !PathsEqual(previous.InstalledPath, target)
                    && File.Exists(previous.InstalledPath))
                {
                    File.Delete(previous.InstalledPath);
                }

                _store.Save(record with { InstalledPath = target, BackupPath = backup });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SCMW.Core.Logging.Log.Warn($"Installed the mod, but a tidy-up step failed ({ex.GetType().Name}).");
            }

            return new InstallOutcome(true, target, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var kind = WriteFailure.Classify(ex);
            return new InstallOutcome(false, null,
                WriteFailure.Message(kind, "install the mod into SCUM"), kind);
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public RemoveOutcome Remove(string gamePath, string projectId)
    {
        var record = _store.Load(projectId);
        if (record is null) return new RemoveOutcome(true, null);

        try
        {
            if (File.Exists(record.InstalledPath)) File.Delete(record.InstalledPath);
            if (record.BackupPath is not null && File.Exists(record.BackupPath))
                File.Copy(record.BackupPath, record.InstalledPath, true);
            _store.Clear(projectId);
            return new RemoveOutcome(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var kind = WriteFailure.Classify(ex);
            return new RemoveOutcome(false,
                WriteFailure.Message(kind, "remove the mod from SCUM"), kind);
        }
    }

    /// <summary>True when this project currently has a record of being installed.</summary>
    public bool IsInstalled(string projectId) => _store.Load(projectId) is not null;
}
