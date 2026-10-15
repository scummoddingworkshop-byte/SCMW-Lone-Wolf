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
