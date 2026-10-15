namespace SCMW.GameModel;

/// <summary>
/// Safety checks before the reader writes anything: don't scribble into a folder
/// that isn't ours, and compare two paths the way the filesystem does.
/// </summary>
internal static class OutputGuard
{
    private static readonly string[] NotebookMarkers = { "README.md", "index.md", "meanings.md", "links.md" };

    /// <summary>Null when it is safe to write into <paramref name="outDir"/>, else a plain reason.</summary>
    internal static string? Check(string outDir, bool limited)
    {
        if (string.IsNullOrWhiteSpace(outDir)) return "No output folder was given.";
        if (!Directory.Exists(outDir)) return null;                                   // we will create it
        if (!Directory.EnumerateFileSystemEntries(outDir).Any()) return null;         // empty

        var looksLikeNotebook =
            NotebookMarkers.Any(marker => File.Exists(Path.Combine(outDir, marker))) ||
            Directory.Exists(Path.Combine(outDir, "fields"));

        if (!looksLikeNotebook)
            return $"'{outDir}' already exists and does not look like the Game Model notebook, so I won't write into it. " +
                   "Point --out at the notebook folder (or an empty one).";

        // A --limit trial must never clobber a real notebook.
        if (limited && !IsLimitedNotebook(outDir))
            return $"'{outDir}' already holds a full notebook, so I won't overwrite it with a --limit trial. " +
                   "Write the trial run somewhere else.";

        return null;
    }

    private static bool IsLimitedNotebook(string outDir)
    {
        var index = Path.Combine(outDir, "index.md");
        return File.Exists(index) &&
               File.ReadAllText(index).Contains("Limited run", StringComparison.Ordinal);
    }

    /// <summary>True when two paths name the same place (separators and letter case ignored).</summary>
    internal static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        static string Normalise(string path) => path.Replace('/', '\\').TrimEnd('\\');
        return string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);
    }
}
