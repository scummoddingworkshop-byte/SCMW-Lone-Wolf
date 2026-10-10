namespace SCMW.Core.Projects;

/// <summary>
/// A single mod project known to the workspace. Persisted as
/// <c>&lt;Workspace&gt;\Projects\&lt;Name&gt; &lt;id8&gt;\project.json</c>;
/// <see cref="FolderPath"/> is derived from the folder that holds that file.
/// </summary>
public sealed record ProjectRecord(string Id, string Name, string Description,
    string FolderPath, DateTime CreatedUtc, DateTime ModifiedUtc, string? IconFileName = null)
{
    /// <summary>
    /// Absolute path to the copied icon image inside <see cref="FolderPath"/>, or null when the
    /// project has no icon. Computed on the fly and never persisted (the file stores only the
    /// relative <see cref="IconFileName"/> so a project keeps working after the folder is moved).
    /// </summary>
    public string? IconPath => IconFileName is null ? null : Path.Combine(FolderPath, IconFileName);
}
