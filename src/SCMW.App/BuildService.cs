using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App;

/// <summary>The app's own name for the real build pipeline, so the view model stays testable.</summary>
public interface IBuildService
{
    Task<BuildOutcome> BuildAsync(string projectName, string version,
        IReadOnlyList<BuildItem> items, IProgress<int>? progress, CancellationToken ct);
    Task<InstallOutcome> InstallAsync(string projectId, string projectName, string version,
        string pakPath, CancellationToken ct);
    Task<RemoveOutcome> RemoveAsync(string projectId, CancellationToken ct);
    bool IsInstalled(string projectId);
}

/// <summary>
/// Wires the backend to the player's real SCUM install: one mounted reader, UAssetAPI writer, the
/// bundled repak, and the per-project install store. Null when no game is configured.
/// </summary>
public sealed class BuildService : IBuildService, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IModBuilder _builder;
    private readonly ModInstaller _installer;
    private readonly PakMount _mount;

    private BuildService(AppSettings settings, IModBuilder builder, ModInstaller installer, PakMount mount)
    {
        _settings = settings;
        _builder = builder;
        _installer = installer;
        _mount = mount;
    }

    public static BuildService? Create(AppSettings settings, WorkspacePaths workspace, IContentSource source)
    {
        var paks = ContentSourceFactory.PakFolderFor(settings, workspace);
        if (paks is null) return null;

        var mount = new PakMount();
        if (!mount.TryMount(paks, settings.AesKey, out _)) { mount.Dispose(); return null; }

        var builder = new ModBuilder(
            new PakMountPackageReader(mount),
            new UAssetPackageWriter(),
            new RepakPacker(Path.Combine(AppContext.BaseDirectory, "tools", "repak", "repak.exe"),
                new SystemProcessRunner()),
            new ContentSourceAssetResolver(source),
            workspace.Builds);

        var installer = new ModInstaller(
            new FileInstallStore(workspace.Builds),
            Path.Combine(workspace.Backups, "mods"));

        return new BuildService(settings, builder, installer, mount);
    }

    public Task<BuildOutcome> BuildAsync(string projectName, string version,
        IReadOnlyList<BuildItem> items, IProgress<int>? progress, CancellationToken ct)
        => _builder.BuildAsync(new BuildRequest(projectName, version, items), progress, ct);

    public Task<InstallOutcome> InstallAsync(string projectId, string projectName, string version,
        string pakPath, CancellationToken ct)
    {
        var record = new InstallRecord(projectId, projectName, version, Path.GetFileName(pakPath),
            pakPath, InstalledPath: "", DateTime.UtcNow, BackupPath: null);
        return Task.FromResult(_installer.Install(_settings.GamePath!, record, pakPath));
    }

    public Task<RemoveOutcome> RemoveAsync(string projectId, CancellationToken ct)
        => Task.FromResult(_installer.Remove(_settings.GamePath!, projectId));

    public bool IsInstalled(string projectId) => _installer.IsInstalled(projectId);

    public void Dispose() => _mount.Dispose();
}
