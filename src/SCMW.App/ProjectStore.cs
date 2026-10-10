using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SCMW.Core.Projects;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App;

/// <summary>
/// The one place the app talks to the engine's project machinery. It adds the two things the
/// engine does not do — "which project is current" (kept in settings) and "duplicate" — and
/// announces every change, so no screen can show a stale list.
/// </summary>
public sealed class ProjectStore
{
    private readonly ProjectService _service;
    private readonly ISettingsStore _settings;

    public ProjectStore(ProjectService service, ISettingsStore settings)
    {
        _service = service;
        _settings = settings;
    }

    /// <summary>
    /// A store over a throwaway workspace, so a bare shell (and every test) never touches the
    /// real documents folder. The app itself wires the real workspace in <c>App.axaml.cs</c>.
    /// </summary>
    public static ProjectStore Default() => new(
        new ProjectService(WorkspaceService.Ensure(
            Path.Combine(Path.GetTempPath(), "SCMW", "workspace"))),
        new InMemorySettingsStore());

    /// <summary>Raised after anything changes. Screens re-read rather than caching.</summary>
    public event Action? Changed;

    /// <summary>Tells screens to re-read — used after a backup puts a project folder back.</summary>
    public void Refresh() => Changed?.Invoke();

    /// <summary>Every project, newest first.</summary>
    public IReadOnlyList<ProjectRecord> All() =>
        _service.List().OrderByDescending(p => p.ModifiedUtc).ToList();

    /// <summary>The current project: the saved one, or the newest if that id is gone.</summary>
    public ProjectRecord? Current
    {
        get
        {
            var all = All();
            var id = _settings.Load().ActiveProjectId;

            if (string.IsNullOrWhiteSpace(id)) return all.FirstOrDefault();
            return all.FirstOrDefault(p => p.Id == id) ?? all.FirstOrDefault();
        }
    }

    public void SetCurrent(string id)
    {
        var settings = _settings.Load();
        _settings.Save(settings with { ActiveProjectId = id });
        Changed?.Invoke();
    }

    /// <summary>
    /// True when the current project was chosen for the player rather than by them — none was
    /// opened (or the saved one is gone), so the newest stands in. Screens can say so instead of
    /// silently re-targeting.
    /// </summary>
    public bool CurrentIsFallback
    {
        get
        {
            var all = All();
            if (all.Count == 0) return false;

            var id = _settings.Load().ActiveProjectId;
            return string.IsNullOrWhiteSpace(id) || all.All(p => p.Id != id);
        }
    }

    public CreateResult Create(string name, string description, string? iconSourcePath = null)
    {
        var result = _service.Create(name, description, iconSourcePath);
        if (result.Project is not null) Changed?.Invoke();
        return result;
    }

    public string? Rename(string id, string name)
    {
        var error = _service.Rename(id, name);
        if (error is null) Changed?.Invoke();
        return error;
    }

    public string? SetDescription(string id, string description)
    {
        var error = _service.SetDescription(id, description);
        if (error is null) Changed?.Invoke();
        return error;
    }

    public string? SetIcon(string id, string? iconSourcePath)
    {
        var error = _service.SetIcon(id, iconSourcePath);
        if (error is null) Changed?.Invoke();
        return error;
    }

    public string? Delete(string id)
    {
        var error = _service.Delete(id);
        if (error is not null) return error;

        // Never leave "current" pointing at something that is gone.
        var settings = _settings.Load();
        if (settings.ActiveProjectId == id)
        {
            _settings.Save(settings with { ActiveProjectId = null });
        }

        Changed?.Invoke();
        return null;
    }

    /// <summary>The engine has no copy, so we make one: same description and picture.</summary>
    public CreateResult Duplicate(string id)
    {
        var source = _service.GetById(id);
        if (source is null) return new CreateResult(null, null, ProjectService.NotFoundMessage);

        var result = _service.Create(source.Name + " copy", source.Description);
        if (result.Project is not null && source.IconPath is not null && File.Exists(source.IconPath))
        {
            _service.SetIcon(result.Project.Id, source.IconPath);
        }

        if (result.Project is not null) Changed?.Invoke();
        return result;
    }
}
