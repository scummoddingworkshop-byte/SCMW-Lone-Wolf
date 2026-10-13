using SCMW.Core.Logging;
using SCMW.Core.Settings;

namespace SCMW.Core.Projects;

/// <summary>
/// The player's currently-open project. The selection is persisted as a bare id in
/// <see cref="AppSettings.ActiveProjectId"/> (so it survives a restart) and resolved back to the
/// real project on demand. Every method is safe to call when no project is open: an unknown,
/// deleted or unreadable id simply resolves to null. A stored id that no longer matches a
/// project is cleared with one plain warning — it is never left dangling and never throws.
/// Core-only: no UI, no content backend.
/// </summary>
public sealed class ActiveProjectService
{
    private readonly ISettingsStore? _store;
    private readonly ProjectService? _projects;

    /// <param name="store">Settings store the active id is persisted in; null = nothing persists.</param>
    /// <param name="projects">Project lookup used to resolve/verify the stored id; null = cannot resolve.</param>
    public ActiveProjectService(ISettingsStore? store, ProjectService? projects = null)
    {
        _store = store;
        _projects = projects;
    }

    /// <summary>The active project id currently stored in settings, or null.</summary>
    public string? StoredId
    {
        get
        {
            if (_store is null)
                return null;

            try
            {
                return _store.Load().ActiveProjectId;
            }
            catch (Exception ex)
            {
                Log.Warn("active project: could not read settings: " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// Persist <paramref name="id"/> as the active project. Persisting the same id twice is a
    /// no-op. A store failure is a plain warning (the in-memory selection still works for the
    /// current session).
    /// </summary>
    public void Set(string id)
    {
        if (_store is null)
            return;

        // A blank id means "no project open" — clear it rather than silently doing nothing.
        if (string.IsNullOrWhiteSpace(id))
        {
            Clear();
            return;
        }

        try
        {
            var current = _store.Load();
            if (!string.Equals(current.ActiveProjectId, id, StringComparison.Ordinal))
                _store.Save(current with { ActiveProjectId = id });
        }
        catch (Exception ex)
        {
            Log.Warn("active project: could not save selection: " + ex.Message);
        }
    }

    /// <summary>Forget the active project (persisted as null).</summary>
    public void Clear()
    {
        if (_store is null)
            return;

        try
        {
            var current = _store.Load();
            if (current.ActiveProjectId is not null)
                _store.Save(current with { ActiveProjectId = null });
        }
        catch (Exception ex)
        {
            Log.Warn("active project: could not clear selection: " + ex.Message);
        }
    }

    /// <summary>
    /// Set the active project only when <paramref name="id"/> still resolves to a real project.
    /// Returns the activated project, or null when it is unknown/deleted — in which case any
    /// previously stored id is cleared so the header can honestly say "No project open".
    /// </summary>
    public ProjectRecord? TryActivate(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var project = _projects?.GetById(id);
        if (project is null)
        {
            Clear();
            return null;
        }

        Set(id);
        return project;
    }

    /// <summary>
    /// Resolve the stored id to its project. Returns null when nothing is open, no lookup is
    /// available, or the stored id no longer matches a project (in which case the stale id is
    /// cleared and a plain warning is logged). Never throws.
    /// </summary>
    public ProjectRecord? Resolve()
    {
        var id = StoredId;
        if (string.IsNullOrWhiteSpace(id) || _projects is null)
            return null;

        ProjectRecord? project;
        try
        {
            project = _projects.GetById(id);
        }
        catch (Exception ex)
        {
            Log.Warn("active project: could not resolve " + id + ": " + ex.Message);
            return null;
        }

        if (project is null)
        {
            Log.Warn("active project " + id + " no longer exists; clearing it");
            Clear();
            return null;
        }

        return project;
    }
}
