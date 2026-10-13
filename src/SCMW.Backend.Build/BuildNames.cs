using System.Text.RegularExpressions;

namespace SCMW.Backend.Build;

/// <summary>Every place a name or path is derived, in one spot, so they cannot drift apart.</summary>
public static class BuildNames
{
    private static readonly Regex ValidVersion = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    /// <summary>A Windows-safe file/folder name: letters and digits kept, runs of others → one '-'.</summary>
    public static string SafeName(string projectName)
    {
        var mapped = new string((projectName ?? "").Trim()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var collapsed = Regex.Replace(mapped, "-+", "-").Trim('-');
        return collapsed.Length == 0 ? "mod" : collapsed;
    }

    public static string NormalizeVersion(string version)
    {
        var trimmed = (version ?? "").Trim().TrimStart('v', 'V');
        return ValidVersion.IsMatch(trimmed) ? trimmed : "1.0.0";
    }

    /// <summary>True when a version reads like <c>1.2.3</c> (an optional leading "v" is allowed).</summary>
    public static bool IsValidVersion(string? version)
    {
        var trimmed = (version ?? "").Trim().TrimStart('v', 'V');
        return ValidVersion.IsMatch(trimmed);
    }

    /// <summary>Our patch pak: `SCMW_&lt;SafeName&gt;_v&lt;x.y.z&gt;_P.pak` (the `_P` makes SCUM replace the original).</summary>
    public static string PakFileName(string projectName, string version)
        => $"SCMW_{SafeName(projectName)}_v{NormalizeVersion(version)}_P.pak";

    public static string BuildFolder(string buildsRoot, string projectName)
        => Path.Combine(buildsRoot, SafeName(projectName));

    public static string StagingFor(string buildFolder) => Path.Combine(buildFolder, "staging");

    /// <summary>SCUM loads loose patch paks from `&lt;game&gt;\SCUM\Content\Paks\~mods`.</summary>
    public static string ModsFolder(string gamePath)
        => Path.Combine(gamePath, "SCUM", "Content", "Paks", "~mods");

    /// <summary>True only for another version of THIS project's pak — never a prefix look-alike or another tool's.</summary>
    public static bool IsOlderPakOfProject(string fileName, string projectName)
    {
        var pattern = "^SCMW_" + Regex.Escape(SafeName(projectName)) + @"_v\d+\.\d+\.\d+_P\.pak$";
        return Regex.IsMatch(fileName ?? "", pattern, RegexOptions.IgnoreCase);
    }

    private static readonly Regex OurPak = new(@"^SCMW_.+_v\d+\.\d+\.\d+_P\.pak$", RegexOptions.IgnoreCase);

    /// <summary>True for a pak THIS app made (any project) — never a mod from somewhere else.</summary>
    public static bool IsOurPak(string fileName) => OurPak.IsMatch(fileName ?? "");
}
