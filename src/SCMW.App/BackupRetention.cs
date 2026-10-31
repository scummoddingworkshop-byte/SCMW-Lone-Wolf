using System;
using System.Collections.Generic;
using System.IO;

namespace SCMW.App;

/// <summary>
/// The retention rule for the copies SCMW — Lone Wolf keeps: only the newest few of each thing are
/// kept, so the backups folder can never grow into a pile. Trimming is best effort — a copy we cannot
/// delete is simply left for next time, and never becomes an error the player has to deal with.
/// </summary>
internal static class BackupRetention
{
    /// <summary>
    /// How many copies of each thing are kept. The game keeps its own save copies too, and an older
    /// build of a mod is rarely wanted, so a short stack is plenty.
    /// </summary>
    internal const int Keep = 3;

    /// <summary>Only the newest copy of each mod is kept: a mod's older builds are clutter.</summary>
    internal const int KeepMod = 1;

    /// <summary>A project keeps a few copies, in case a delete was a mistake made twice.</summary>
    internal const int KeepProject = 3;

    /// <summary>
    /// Keeps the first of each <paramref name="keyOf"/> and deletes the rest — used for identical
    /// copies of one save, so a save copied three times in a row is shown (and kept) once.
    /// </summary>
    internal static IReadOnlyList<T> KeepFirstPer<T>(IReadOnlyList<T> newestFirst,
                                                     Func<T, string> keyOf, Func<T, string> pathOf)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<T>(newestFirst.Count);

        foreach (var item in newestFirst)
        {
            if (seen.Add(keyOf(item))) kept.Add(item);
            else DeleteQuietly(pathOf(item));
        }

        return kept;
    }

    /// <summary>
    /// Keeps the first <paramref name="keep"/> of <paramref name="newestFirst"/> and deletes the rest
    /// (each is a file or a folder). Returns the entries that were kept, so a caller can show exactly
    /// those even if a delete was refused.
    /// </summary>
    internal static IReadOnlyList<T> KeepNewest<T>(IReadOnlyList<T> newestFirst, int keep,
                                                   Func<T, string> pathOf)
    {
        if (newestFirst.Count <= keep) return newestFirst;

        for (var i = keep; i < newestFirst.Count; i++) DeleteQuietly(pathOf(newestFirst[i]));

        var kept = new List<T>(keep);
        for (var i = 0; i < keep; i++) kept.Add(newestFirst[i]);
        return kept;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Security.SecurityException)
        {
            // Left for next time; the screen still shows only the kept copies.
        }
    }
}
