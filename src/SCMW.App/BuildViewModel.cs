using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.Backend.Build;
using SCMW.Core.Content;

namespace SCMW.App;

/// <summary>
/// Build's brain: the project's name and version, the current project's saved changes, the nudge
/// when there is nothing to build, and the simulated run (idle → running → succeeded
/// or failed). It knows nothing about pixels.
/// </summary>
public partial class BuildViewModel : ObservableObject
{
    private readonly ItemChangeStore? _changes;
    private readonly INavigation _nav;
    private readonly TimeSpan? _stepInterval;
    private readonly ModList? _mods;
    private readonly ProjectStore? _projects;
    private IBuildService? _service;
    private IGameLauncher? _launcher;
    private readonly IContentSource? _source;
    private readonly IElevation? _elevation;
    private DispatcherTimer? _timer;
    private int _stepIndex;
    private IReadOnlyList<BuildChange> _lines = Array.Empty<BuildChange>();
    private string? _builtStamp;       // fingerprint of the last real build (version + changes)
    private string? _installedStamp;   // the stamp that was last installed into SCUM

    public BuildViewModel(ItemChangeStore? changes = null, INavigation? nav = null,
                          TimeSpan? stepInterval = null, ModList? mods = null,
                          ProjectStore? projects = null, IBuildService? service = null,
                          IGameLauncher? launcher = null, IContentSource? source = null,
                          IElevation? elevation = null)
    {
        _changes = changes;
        _nav = nav ?? NavigationHost.Shared;
        _stepInterval = stepInterval;
        _mods = mods;
        _projects = projects;
        _service = service;
        _launcher = launcher;
        _source = source;
        _elevation = elevation;

        if (_projects is not null)
        {
            _projects.Changed += OnProjectsChanged;
            SyncProjectName();
            HasInstalled = _service?.IsInstalled(_projects.Current?.Id ?? "") ?? false;
        }

        if (_changes is not null) _changes.Changed += OnStoreChanged;

        Steps = new[] { "Prepare", "Check", "Build", "Package", "Ready" }
            .Select(label => new ModBuildStep(label))
            .ToList();

        RefreshChanges();
    }

    [ObservableProperty] private string _projectName = "My first mod";

    /// <summary>What the Advanced "Rename" box edits; applied only when the player commits.</summary>
    [ObservableProperty] private string _renameDraft = "";

    /// <summary>Keeps the shown name (and the rename draft) in step with the real current project.</summary>
    private void SyncProjectName()
    {
        ProjectName = _projects!.Current?.Name ?? "No project open";
        RenameDraft = _projects.Current?.Name ?? "";
    }

    /// <summary>Renames the current project once, when the player finishes editing — not per keystroke.</summary>
    [RelayCommand]
    private void CommitRename()
    {
        var current = _projects?.Current;
        if (current is null) return;

        var name = RenameDraft.Trim();
        if (name.Length == 0 || name == current.Name) return;

        _projects!.Rename(current.Id, name);   // raises Changed → SyncProjectName re-reads the name
    }

    /// <summary>True when the shown project was chosen for the player, not by them (say so).</summary>
    public bool ShowProjectFallback => _projects?.CurrentIsFallback ?? false;

    /// <summary>The current project changed: follow its name and re-read its records.</summary>
    private void OnProjectsChanged()
    {
        SyncProjectName();
        OnPropertyChanged(nameof(ShowProjectFallback));
        ResetOutcomeAndRefresh();
    }

    /// <summary>The project's records changed: any outcome no longer describes them.</summary>
    private void OnStoreChanged() => ResetOutcomeAndRefresh();

    private void ResetOutcomeAndRefresh()
    {
        RefreshChanges();

        // A real build in flight is not cancelled by a change; its completion check discards a
        // stale result. (The bare/simulated screen keeps its old "a change cancels the build".)
        if (_service is not null && State == BuildState.Running) return;

        if (State != BuildState.Idle)
        {
            StopTimer();
            State = BuildState.Idle;
        }

        BuiltPakPath = null;
        BuiltVersion = null;
        _builtStamp = null;
        _installedStamp = null;
        HasInstalled = _service?.IsInstalled(_projects?.Current?.Id ?? "") ?? false;
        NotifyInstallState();
    }

    /// <summary>
    /// Re-reads the current project's records from disk and maps them to the list Build shows.
    /// One source of truth: the same records drive Items' values and this list.
    /// </summary>
    private void RefreshChanges()
    {
        var current = _projects?.Current;
        if (current is null || _changes is null)
        {
            _lines = Array.Empty<BuildChange>();
        }
        else
        {
            // One row per item, with all of its changed values listed. Each value shows the latest
            // recorded new value against the value the field had before any record (the first
            // record's old value). This is the same source of truth as Items' "was …", so the two
            // screens cannot drift.
            _lines = _changes.For(current.FolderPath)
                .GroupBy(r => r.AssetId)
                .Select(g => new BuildChange(
                    g.Key,
                    g.GroupBy(r => r.FieldTechnicalName)
                        .Select(f => new BuildValueChange(
                            SCMW.Core.Content.FieldNames.Friendly(f.Key),
                            f.First().OldValue ?? "",
                            f.Last().NewValue))
                        .ToList()))
                .ToList();
        }

        OnPropertyChanged(nameof(Changes));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ShowNudge));
        OnPropertyChanged(nameof(CanBuild));
        BuildCommand.NotifyCanExecuteChanged();

        // Item pictures are a fast follow: the list is usable without them.
        _ = RequestIconsAsync();
    }

    /// <summary>Removes every change for one item, so it reverts to the game's own values.</summary>
    [RelayCommand]
    private void RemoveItem(BuildChange? row)
    {
        var current = _projects?.Current;
        if (row is null || current is null || _changes is null) return;

        _changes.RemoveItem(current.FolderPath, row.ItemName);   // raises Changed → refresh
    }

    /// <summary>
    /// Resolves each changed item's picture from the content source (search by name, extract, read
    /// the cached path). Never throws: a miss leaves the fallback mark.
    /// </summary>
    public async Task RequestIconsAsync()
    {
        if (_source is null) return;

        try
        {
            foreach (var row in _lines.ToList())
            {
                var hits = await _source.SearchAsync(row.ItemName, null, 0, 20).ConfigureAwait(false);
                var hit = hits.FirstOrDefault(a => string.Equals(a.Name, row.ItemName, StringComparison.OrdinalIgnoreCase))
                          ?? hits.FirstOrDefault();
                if (hit is null) continue;

                await _source.ExtractIconsAsync(new[] { hit.Id }).ConfigureAwait(false);
                var path = _source.Icons.GetIconPath(hit.Id);
                if (string.IsNullOrEmpty(path)) continue;

                var picture = LoadPicture(path!);
                if (picture is null) continue;

                RunOnUi(() => row.Picture = picture);
            }
        }
        catch
        {
            // Pictures are optional — a failure simply leaves the fallback mark.
        }
    }

    /// <summary>Runs an action on the UI thread (the icon pass awaits off-thread).</summary>
    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private static IImage? LoadPicture(string path)
    {
        try { return File.Exists(path) ? new Bitmap(path) : null; }
        catch { return null; }
    }

    [ObservableProperty] private string _modVersion = "1.0.0";
    [ObservableProperty] private BuildState _state = BuildState.Idle;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _failureWhat = "";
    [ObservableProperty] private string _failureMeaning = "";
    [ObservableProperty] private string _failureNext = "";
    [ObservableProperty] private string _failureDetails = "";
    [ObservableProperty] private string? _builtPakPath;
    [ObservableProperty] private string? _builtVersion;
    [ObservableProperty] private bool _hasInstalled;

    /// <summary>True when the last failure was a Windows permission block the player can fix by
    /// restarting with administrator rights.</summary>
    [ObservableProperty] private bool _canRestartAsAdmin;

    /// <summary>True only when there is technical detail to show, so an empty expander never appears.</summary>
    public bool HasFailureDetails => !string.IsNullOrWhiteSpace(FailureDetails);

    // Real-build state (only when a service is wired in; the bare screen keeps the old simulation).

    /// <summary>True when a build exists that is not (yet) the copy sitting in SCUM.</summary>
    public bool HasBuildToInstall =>
        _service is not null && BuiltPakPath is not null && !InstalledMatchesBuild;

    /// <summary>True when the copy in SCUM is exactly the build we last made.</summary>
    public bool InstalledMatchesBuild =>
        HasInstalled && _builtStamp is not null &&
        string.Equals(_builtStamp, _installedStamp, StringComparison.Ordinal);

    public bool CanInstall => HasBuildToInstall && State != BuildState.Running;
    public bool CanRemove => _service is not null && HasInstalled;

    /// <summary>Show the install button whenever there is a build SCUM does not have yet.</summary>
    public bool ShowInstallButton => HasBuildToInstall;

    /// <summary>"Install" the first time; "Update" once SCUM already has an older copy.</summary>
    public string InstallButtonLabel => HasInstalled ? "Update the copy in SCUM" : "Install into SCUM";

    public string SuccessMessage =>
        !HasInstalled ? "Your mod is built and ready to install."
        : HasBuildToInstall ? "Your mod is built — SCUM still has the earlier copy."
        : "Your mod is ready and installed into SCUM.";

    /// <summary>A plain line getting ahead of the obvious question: what do these buttons do?</summary>
    public string InstallHint =>
        !HasBuildToInstall ? ""
        : HasInstalled ? "Updating replaces the old copy in SCUM — no need to remove it first."
        : "Installing copies the mod into SCUM. Then launch SCUM to try it.";

    public bool ShowInstallHint => InstallHint.Length > 0;

    /// <summary>Re-tells the screen everything that depends on build/install state, in one place.</summary>
    private void NotifyInstallState()
    {
        OnPropertyChanged(nameof(HasBuildToInstall));
        OnPropertyChanged(nameof(InstalledMatchesBuild));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(InstallButtonLabel));
        OnPropertyChanged(nameof(SuccessMessage));
        OnPropertyChanged(nameof(InstallHint));
        OnPropertyChanged(nameof(ShowInstallHint));
        InstallCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A game swap asked for while a build was running, applied when it ends.</summary>
    private (IBuildService? Service, IGameLauncher? Launcher)? _pendingUseGame;

    /// <summary>
    /// Swaps in the real game engine once the player has configured SCUM — which may happen after
    /// this screen was built. Any practice build and stale state are cleared so the screen starts
    /// fresh against the real game.
    /// </summary>
    public void UseGame(IBuildService? service, IGameLauncher? launcher)
    {
        // Never swap the engine out from under a build that is still running; do it when it ends.
        if (State == BuildState.Running)
        {
            _pendingUseGame = (service, launcher);
            return;
        }

        ApplyGame(service, launcher);
    }

    private void ApplyGame(IBuildService? service, IGameLauncher? launcher)
    {
        _service = service;
        _launcher = launcher;
        BuiltPakPath = null;
        _builtStamp = null;
        _installedStamp = null;
        HasInstalled = service?.IsInstalled(_projects?.Current?.Id ?? "") ?? false;
        CanRestartAsAdmin = false;
        State = BuildState.Idle;
        NotifyInstallState();
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(ShowLaunchNote));
        LaunchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Applies a game swap that was deferred because a build was running.</summary>
    private void FlushPendingUseGame()
    {
        if (_pendingUseGame is { } pending)
        {
            _pendingUseGame = null;
            ApplyGame(pending.Service, pending.Launcher);
        }
    }

    public IReadOnlyList<ModBuildStep> Steps { get; }

    /// <summary>What's in this mod: the current project's saved records, oldest first.</summary>
    public IReadOnlyList<BuildChange> Changes => _lines;

    public bool HasChanges => _lines.Count > 0;
    public bool ShowNudge => !HasChanges;
    public bool CanBuild => _projects?.Current is not null && HasChanges && State != BuildState.Running;
    public bool IsRunning => State == BuildState.Running;
    public bool IsSucceeded => State == BuildState.Succeeded;
    public bool IsFailed => State == BuildState.Failed;
    public bool ShowStepBar => State == BuildState.Running;

    partial void OnStateChanged(BuildState value)
    {
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsSucceeded));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(ShowStepBar));
        NotifyInstallState();
        BuildCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasInstalledChanged(bool value) => NotifyInstallState();

    partial void OnFailureDetailsChanged(string value) => OnPropertyChanged(nameof(HasFailureDetails));

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        var project = _projects?.Current;
        if (_service is null || project is null)
        {
            // The bare/preview screen keeps the original simulated run.
            State = BuildState.Running;
            _stepIndex = 0;
            UpdateSteps();
            if (_stepInterval is { } interval) StartTimer(interval);
            return;
        }

        if (!SCMW.Backend.Build.BuildNames.IsValidVersion(ModVersion))
        {
            ShowFailure("That version isn't usable.",
                "The mod version should look like 1.0.0 — three numbers separated by dots.",
                "Change the version on the Build screen, then press Build again.", "");
            return;
        }

        var projectId = project.Id;
        var records = _changes?.For(project.FolderPath) ?? Array.Empty<SCMW.Core.Editing.ChangeRecord>();
        var token = RecordsToken(records);

        State = BuildState.Running;
        _stepIndex = 0;
        UpdateSteps();

        var progress = new Progress<int>(i => { _stepIndex = Math.Clamp(i, 0, Steps.Count - 1); UpdateSteps(); });

        SCMW.Backend.Build.BuildOutcome outcome;
        try
        {
            outcome = await _service.BuildAsync(project.Name, ModVersion, BuildItemsFrom(records), progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A backend surprise (a missing tool, a locked file) must be one plain message, not a crash.
            SCMW.Core.Logging.Log.Warn($"Build failed unexpectedly ({ex.GetType().Name}).");
            ShowFailure("The mod could not be made.",
                "Something unexpected went wrong while reading or writing the game files.",
                "Try again. If it keeps happening, close anything using SCUM's files and retry.", "");
            return;
        }

        // If the project or the changes moved while we were building, this outcome is stale.
        var currentRecords = _changes?.For(project.FolderPath) ?? Array.Empty<SCMW.Core.Editing.ChangeRecord>();
        if (_projects?.Current?.Id != projectId || RecordsToken(currentRecords) != token)
        {
            ShowFailure("Your changes changed while the mod was being made.",
                "That build was for an earlier set of changes, so it was set aside and not offered to install.",
                "Press Build again to make the mod from your latest changes.", "");
            return;
        }

        if (outcome.Ok && outcome.PakPath is not null)
        {
            BuiltPakPath = outcome.PakPath;
            BuiltVersion = ModVersion;
            FinishSuccess();
        }
        else
        {
            var error = outcome.Error ?? "Something in the game files was not in the shape we expected.";

            // Only offer the folder hint when the problem really is the game folder; otherwise the
            // error itself is the actionable message (e.g. an item name that matches more than one).
            var next = error.Contains("SCUM folder", StringComparison.OrdinalIgnoreCase)
                       || error.Contains("SCUM's files", StringComparison.OrdinalIgnoreCase)
                ? "Check your SCUM folder in Settings, then try again."
                : "";

            ShowFailure("The mod could not be made.", error, next, "");
        }
    }

    /// <summary>
    /// One item per recorded asset. A list change (add/remove an entry) is kept in the order it was
    /// recorded; a value edit collapses to the latest value per field. List changes run first, so a
    /// value edit on a slot that a change just added or shifted lands on the right entry.
    /// </summary>
    private static IReadOnlyList<BuildItem> BuildItemsFrom(IReadOnlyList<SCMW.Core.Editing.ChangeRecord> records)
        => records
            // Resolve by the asset's own path when we have it; fall back to the friendly name only
            // for records made before paths were kept. This is what keeps two same-named assets apart.
            .GroupBy(r => r.AssetPath ?? r.AssetId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BuildItem(g.Key, ChangesFor(g)))
            .ToList();

    private static IReadOnlyList<FieldChange> ChangesFor(IEnumerable<SCMW.Core.Editing.ChangeRecord> records)
    {
        var listChanges = new List<FieldChange>();
        var latestValue = new Dictionary<string, SCMW.Core.Editing.ChangeRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (record.Operation == SCMW.Core.Editing.ChangeOperation.Set)
                latestValue[record.FieldTechnicalName] = record;
            else
                listChanges.Add(new FieldChange(record.FieldTechnicalName, record.NewValue, record.Operation));
        }

        var changes = new List<FieldChange>(listChanges);
        foreach (var record in latestValue.Values)
            changes.Add(new FieldChange(record.FieldTechnicalName, record.NewValue));
        return changes;
    }

    /// <summary>A cheap fingerprint of the current project's changes, to detect edits mid-build.</summary>
    private static string RecordsToken(IReadOnlyList<SCMW.Core.Editing.ChangeRecord> records)
        => string.Join("|", records.Select(r => $"{r.AssetPath}\u001f{r.AssetId}\u001f{r.FieldTechnicalName}\u001f{r.Operation}\u001f{r.NewValue}"));

    /// <summary>Copies the last built `.pak` into SCUM. Only available with a real service.</summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        var project = _projects?.Current;
        if (_service is null || project is null || BuiltPakPath is null) return;

        var outcome = await _service.InstallAsync(project.Id, project.Name, BuiltVersion ?? ModVersion,
            BuiltPakPath, CancellationToken.None);
        if (outcome.Ok)
        {
            HasInstalled = true;
            _installedStamp = _builtStamp;
            CanRestartAsAdmin = false;
            NotifyInstallState();
        }
        else
        {
            var needsAdmin = outcome.Failure == WriteFailureKind.PermissionDenied;
            ShowFailure("The mod could not be installed.",
                outcome.Error ?? "The mod could not be copied into SCUM.",
                needsAdmin ? "" : "Close the game, then try again.", "", needsAdmin);
        }
    }

    /// <summary>Removes exactly what we placed, putting SCUM back the way it was.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task Remove()
    {
        var project = _projects?.Current;
        if (_service is null || project is null) return;

        var outcome = await _service.RemoveAsync(project.Id, CancellationToken.None);
        if (outcome.Ok)
        {
            HasInstalled = false;
            _installedStamp = null;
            CanRestartAsAdmin = false;
            NotifyInstallState();
        }
        else
        {
            var needsAdmin = outcome.Failure == WriteFailureKind.PermissionDenied;
            ShowFailure("The mod could not be removed.",
                outcome.Error ?? "The file could not be removed from SCUM.",
                needsAdmin ? "" : "Close the game, then try again.", "", needsAdmin);
        }
    }

    /// <summary>Moves the build on by one step. Called by the timer (and directly by tests).</summary>
    public void Advance()
    {
        if (State != BuildState.Running) return;

        _stepIndex++;
        if (_stepIndex >= Steps.Count)
        {
            FinishSuccess();
            return;
        }

        UpdateSteps();
    }

    /// <summary>Shows the failure state. When <paramref name="needsAdmin"/> is set (and we are not
    /// already elevated) the panel offers to restart with administrator rights.</summary>
    public void ShowFailure(string what, string meaning, string next, string details, bool needsAdmin = false)
    {
        StopTimer();
        FailureWhat = what;
        FailureMeaning = meaning;
        FailureNext = next;
        FailureDetails = details;
        CanRestartAsAdmin = needsAdmin && _elevation is { IsElevated: false };
        State = BuildState.Failed;
        FlushPendingUseGame();
    }

    /// <summary>Reopens SCMW with administrator rights so a blocked write can go through.</summary>
    [RelayCommand]
    private void RestartAsAdmin() => _elevation?.RestartAsAdmin();

    [RelayCommand] private void GoToItems() => _nav.GoTo(ScreenKey.Items);

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task Launch()
    {
        if (_launcher is not null) await _launcher.LaunchModdedAsync();
    }

    /// <summary>True when SCUM can be launched (its program was found).</summary>
    public bool CanLaunch => _launcher?.IsAvailable ?? false;

    /// <summary>True while the "set your SCUM folder" hint should show.</summary>
    public bool ShowLaunchNote => !CanLaunch;

    private void UpdateSteps()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsActive = i == _stepIndex;
            Steps[i].IsDone = i < _stepIndex;
        }

        StatusText = _stepIndex switch
        {
            0 => "Preparing…",
            1 => "Checking your changes…",
            2 => "Making the mod…",
            3 => "Packaging…",
            _ => "Finishing up…",
        };
    }

    private void FinishSuccess()
    {
        foreach (var step in Steps)
        {
            step.IsActive = false;
            step.IsDone = true;
        }

        StopTimer();
        StatusText = "";
        if (_service is null) _mods?.Install(ProjectName);
        _builtStamp = CurrentStamp();
        State = BuildState.Succeeded;
        NotifyInstallState();
        FlushPendingUseGame();
    }

    /// <summary>A fingerprint of what was just built: the version plus the current changes.</summary>
    private string CurrentStamp()
    {
        var project = _projects?.Current;
        if (project is null || _changes is null) return ModVersion;
        return ModVersion + "|" + RecordsToken(_changes.For(project.FolderPath));
    }

    private void StartTimer(TimeSpan interval)
    {
        StopTimer();
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Advance();
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}
