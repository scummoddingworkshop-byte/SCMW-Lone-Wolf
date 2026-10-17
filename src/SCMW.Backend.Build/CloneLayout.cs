namespace SCMW.Backend.Build;

/// <summary>
/// How a cloned asset is laid out: a cooked package is a file named after its asset plus companion
/// files (<c>.uexp</c>, <c>.ubulk</c>, …). Every file whose name is <c>&lt;oldName&gt;.&lt;ext&gt;</c>
/// takes the new asset's name, so the copy is a complete, correctly-named package.
/// </summary>
internal static class CloneLayout
{
    /// <summary>The staged name for one donor file when it is cloned as <paramref name="newName"/>.</summary>
    public static string StagedFileName(string fileName, string oldName, string newName)
    {
        var prefix = oldName + ".";
        return fileName.StartsWith(prefix, StringComparison.Ordinal)
            ? newName + fileName[oldName.Length..]
            : fileName;
    }
}
