using System.Text.Json;

namespace SCMW.Backend.Build;

/// <summary>One `installed.json` per project under the workspace's Builds folder.</summary>
public sealed class FileInstallStore : IInstallStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _buildsRoot;

    public FileInstallStore(string buildsRoot) => _buildsRoot = buildsRoot;

    private string PathFor(string projectId) => Path.Combine(_buildsRoot, projectId, "installed.json");

    public InstallRecord? Load(string projectId)
    {
        var path = PathFor(projectId);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path)); }
        catch { return null; }
    }

    public void Save(InstallRecord record)
    {
        var path = PathFor(record.ProjectId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(record, Options));
    }

    public void Clear(string projectId)
    {
        var path = PathFor(projectId);
        if (File.Exists(path)) File.Delete(path);
    }
}
