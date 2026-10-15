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
    private readonly IContentSource? _source;
    private readonly IElevation? _elevation;
    private readonly ISaveBackupsService? _saves;
    private DispatcherTimer? _timer;
    private int _stepIndex;

    public BuildViewModel(ItemChangeStore? changes = null, INavigation? nav = null,
                          TimeSpan? stepInterval = null, ModList? mods = null,
                          ProjectStore? projects = null, IBuildService? service = null,
                          IContentSource? source = null,
                          IElevation? elevation = null, ISaveBackupsService? saves = null)
    {
        _changes = changes;
        _nav = nav ?? NavigationHost.Shared;
        _stepInterval = stepInterval;
        _mods = mods;
        _projects = projects;
        _service = service;
        _source = source;
        _elevation = elevation;
        _saves = saves;

        // The preview/diff shown on this screen — the same records Build writes, one home for the diff.
        Preview = new PreviewViewModel(_changes, _projects, _source);

        if (_projects is not null)
        {
            _projects.Changed += OnProjectsChanged;
            SyncProjectName();
        }

        if (_changes is not null) _changes.Changed += OnStoreChanged;

        Steps = new[] { "Prepare", "Check", "Build", "Package", "Ready" }
            .Select(label => new ModBuildStep(label))
            .ToList();

        RefreshChanges();
    }

    [ObservableProperty] private string _projectName = "My first mod";

    /// <summary>A plain note about the automatic save backup taken before installing (empty when fine).</summary>
    [ObservableProperty] private string _saveNote = "";

    /// <summary>Keeps the shown name in step with the chosen project. (Renaming lives on Projects.)</summary>
    private void SyncProjectName() =>
        ProjectName = _projects!.Chosen?.Name ?? "No project open";

    /// <summary>True when no project is chosen yet — Build builds a chosen mod, never a fall-back.</summary>
    public bool NeedsProject => _projects is not null && !_projects.HasChosenProject;

    /// <summary>The chosen project changed: follow its name and re-read its records.</summary>
    private void OnProjectsChanged()
    {
        SyncProjectName();
        OnPropertyChanged(nameof(NeedsProject));
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
    }

    /// <summary>
    /// Re-reads the current project's records through the preview (one home for the diff), and keeps
    /// Build's own flags in step.
    /// </summary>
    private void RefreshChanges()
    {
        Preview.Refresh();

        OnPropertyChanged(nameof(Changes));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ShowNudge));
        OnPropertyChanged(nameof(CanBuild));
        BuildCommand.NotifyCanExecuteChanged();
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

    /// <summary>True when the last failure was a Windows permission block the player can fix by
    /// restarting with administrator rights.</summary>
    [ObservableProperty] private bool _canRestartAsAdmin;

    /// <summary>True only when there is technical detail to show, so an empty expander never appears.</summary>
    public bool HasFailureDetails => !string.IsNullOrWhiteSpace(FailureDetails);

    /// <summary>What the success panel says: the mod is in SCUM, switched off, waiting to be enabled.</summary>
    public string SuccessMessage =>
        "Your mod is built and ready to be enabled in the Mods Management screen.";

    /// <summary>A game swap asked for while a build was running, applied when it ends.</summary>
    private bool _hasPendingUseGame;
    private IBuildService? _pendingService;

    /// <summary>
    /// Swaps in the real game engine once the player has configured SCUM — which may happen after
    /// this screen was built. Any practice build and stale state are cleared so the screen starts
    /// fresh against the real game.
    /// </summary>
    public void UseGame(IBuildService? service)
    {
        // Never swap the engine out from under a build that is still running; do it when it ends.
        if (State == BuildState.Running)
        {
            _hasPendingUseGame = true;
            _pendingService = service;
            return;
        }

        ApplyGame(service);
    }

    private void ApplyGame(IBuildService? service)
    {
        _service = service;
        BuiltPakPath = null;
        CanRestartAsAdmin = false;
        State = BuildState.Idle;
    }

    /// <summary>Applies a game swap that was deferred because a build was running.</summary>
    private void FlushPendingUseGame()
    {
        if (!_hasPendingUseGame) return;

        _hasPendingUseGame = false;
        var service = _pendingService;
        _pendingService = null;
        ApplyGame(service);
    }

    public IReadOnlyList<ModBuildStep> Steps { get; }

    /// <summary>The preview/diff shown on this screen: the chosen project's records, grouped by item.</summary>
    public PreviewViewModel Preview { get; }

    /// <summary>What's in this mod: the current project's saved records, oldest first.</summary>
    public IReadOnlyList<BuildChange> Changes => Preview.Changes;

    public bool HasChanges => Preview.HasChanges;
    public bool ShowNudge => !HasChanges;
    public bool CanBuild => _projects?.Chosen is not null && HasChanges && State != BuildState.Running;
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
        BuildCommand.NotifyCanExecuteChanged();
    }

    partial void OnFailureDetailsChanged(string value) => OnPropertyChanged(nameof(HasFailureDetails));

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        var project = _projects?.Chosen;
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
        if (_projects?.Chosen?.Id != projectId || RecordsToken(currentRecords) != token)
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

            // A build lands in SCUM switched off. Nothing new starts affecting the game until the
            // player switches it on in Mods Management.
            var placed = await _service!.InstallAsync(project.Id, project.Name, ModVersion,
                outcome.PakPath, enabled: false, CancellationToken.None);
            if (!placed.Ok)
            {
                var needsAdmin = placed.Failure == WriteFailureKind.PermissionDenied;
                ShowFailure("The mod was made, but it could not be put into SCUM.",
                    placed.Error ?? "The file could not be copied into SCUM's mods folder.",
                    needsAdmin ? "" : "Check your SCUM folder in Settings, then press Build again.", "",
                    needsAdmin);
                return;
            }

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
    /// recorded; a value edit collapses to the latest value per field, and a multiply is worked out
    /// to the number it produces (the same rule the preview shows). List changes run first, so a
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
        var valueChanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            switch (record.Operation)
            {
                case SCMW.Core.Editing.ChangeOperation.Set:
                    valueChanges[record.FieldTechnicalName] = record.NewValue;
                    break;

                // A multiply records a factor, not a value: work it out against the game's own value —
                // the same rule the preview shows — so what is written is the finished number.
                case SCMW.Core.Editing.ChangeOperation.Multiply:
                    if (SCMW.Core.Editing.Multiplier.TryParse(record.NewValue, out var factor))
                    {
                        valueChanges[record.FieldTechnicalName] =
                            SCMW.Core.Editing.Multiplier.Apply(record.OldValue, factor) ?? record.NewValue;
                    }
                    break;

                default:
                    listChanges.Add(new FieldChange(record.FieldTechnicalName, record.NewValue,
                                                    record.Operation, record.DonorPath));
                    break;
            }
        }

        var changes = new List<FieldChange>(listChanges);
        foreach (var value in valueChanges)
            changes.Add(new FieldChange(value.Key, value.Value));
        return changes;
    }

    /// <summary>A cheap fingerprint of the current project's changes, to detect edits mid-build.</summary>
    private static string RecordsToken(IReadOnlyList<SCMW.Core.Editing.ChangeRecord> records)
        => string.Join("|", records.Select(r => $"{r.AssetPath}\u001f{r.AssetId}\u001f{r.FieldTechnicalName}\u001f{r.Operation}\u001f{r.NewValue}"));

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

    [RelayCommand] private void GoToItems() => _nav.GoTo(ScreenKey.Workspace);

    /// <summary>Where a built mod is installed, switched off or removed — the Mods screen.</summary>
    [RelayCommand] private void GoToMods() => _nav.GoTo(ScreenKey.Mods);

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

    /// <summary>
    /// Keep a copy of the saved game before a mod is installed (design §11): a pak never touches the
    /// game's files, but a mod can leave a mark in a save. Best-effort — a failure is said plainly,
    /// never blocking the install.
    /// </summary>
    private void BackUpSaveQuietly()
    {
        if (_saves is null) return;
        SaveNote = _saves.BackUp() ?? "";
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
        BackUpSaveQuietly();
        if (_service is null) _mods?.Place(ProjectName);
        State = BuildState.Succeeded;
        FlushPendingUseGame();
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
