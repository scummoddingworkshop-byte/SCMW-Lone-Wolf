using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.Backend.Build;

namespace SCMW.App;

/// <summary>
/// Mods' brain. With a real <see cref="IModsService"/> it lists everything in SCUM's mods folder,
/// switches each on/off and removes it. Without one (a bare/preview screen) it falls back to the
/// in-memory <see cref="ModList"/> so nothing already working changes.
/// </summary>
public partial class ModsViewModel : ObservableObject
{
    private readonly ModList _mods;
    private readonly INavigation _nav;
    private IModsService? _service;
    private readonly IElevation? _elevation;
    private readonly ObservableCollection<InstalledMod> _real = new();
    private bool _syncing;
    private int _added;

    public ModsViewModel(ModList? mods = null, INavigation? nav = null, IModsService? service = null,
                         IElevation? elevation = null)
    {
        _mods = mods ?? new ModList();
        _nav = nav ?? NavigationHost.Shared;
        _service = service;
        _elevation = elevation;
        _mods.PropertyChanged += (_, _) => RaiseAll();
        LoadFromService();

        if (_service is { IsAvailable: false })
            Message = "Set your SCUM folder in Settings to manage mods.";
    }

    /// <summary>Re-reads the folder and records. Called when the screen is opened.</summary>
    public void Refresh() => LoadFromService();

    /// <summary>
    /// Swaps in the real mods service once the player has configured SCUM — which may happen after
    /// this screen was built. Re-reads the folder and clears any stale message.
    /// </summary>
    public void UseService(IModsService? service)
    {
        _service = service;
        CanRestartAsAdmin = false;
        Message = "";
        LoadFromService();
        if (_service is { IsAvailable: false })
            Message = "Set your SCUM folder in Settings to manage mods.";
        RaiseAll();
    }

    public IReadOnlyList<InstalledMod> Mods => _service is not null ? _real : _mods.Mods;

    public string Summary => _service is not null
        ? $"{Mods.Count} {(Mods.Count == 1 ? "mod" : "mods")} installed · " +
          $"{Mods.Count(m => m.IsOn)} switched on."
        : _mods.Summary;

    public bool HasMods => Mods.Count > 0;
    public bool ShowEmptyState => Mods.Count == 0;

    /// <summary>A plain line for the last thing that happened (a refusal, an import).</summary>
    [ObservableProperty] private string _message = "";

    /// <summary>True when the line is good news, so it shows in the app's success green.</summary>
    [ObservableProperty] private bool _isMessageGood;

    // Every message starts out as "not good news"; a success marks itself good right after setting it.
    partial void OnMessageChanged(string value) => IsMessageGood = false;

    /// <summary>True when the last refusal was a Windows permission block the player can fix by
    /// restarting with administrator rights.</summary>
    [ObservableProperty] private bool _canRestartAsAdmin;

    /// <summary>Reopens SCMW with administrator rights so a blocked write can go through.</summary>
    [RelayCommand]
    private void RestartAsAdmin() => _elevation?.RestartAsAdmin();

    /// <summary>Whether this failure is one a restart with administrator rights would fix.</summary>
    private bool NeedsAdmin(WriteFailureKind kind) =>
        kind == WriteFailureKind.PermissionDenied && _elevation is { IsElevated: false };

    /// <summary>Rebuilds the rows from the service. Also used after any change on disk.</summary>
    private void LoadFromService()
    {
        if (_service is null) return;

        foreach (var mod in _real) mod.PropertyChanged -= OnModChanged;
        _real.Clear();

        foreach (var entry in _service.List())
        {
            var mod = new InstalledMod(entry.DisplayName, entry.IsExternal, entry.IsOn, entry.FileName,
                                       entry.Managed, entry.Location);
            mod.PropertyChanged += OnModChanged;
            _real.Add(mod);
        }

        RaiseAll();
    }

    /// <summary>The row's on/off switch was flipped: rename the file, or put the switch back.</summary>
    private void OnModChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing || _service is null || sender is not InstalledMod mod) return;
        if (e.PropertyName != nameof(InstalledMod.IsOn) || mod.FileName is null) return;

        var result = _service.SetEnabled(mod.FileName, mod.IsOn);
        if (!result.Ok)
        {
            _syncing = true;
            mod.IsOn = !mod.IsOn;                              // put the switch back
            _syncing = false;
            Message = result.Error ?? "That mod could not be switched.";
            CanRestartAsAdmin = NeedsAdmin(result.Failure);
        }
        else
        {
            Message = "";
            CanRestartAsAdmin = false;
            LoadFromService();
        }
    }

    /// <summary>Adds .pak files the player dropped (or chose); refreshes and reports plainly.</summary>
    public System.Threading.Tasks.Task ImportDropped(IReadOnlyList<string> paths)
    {
        if (_service is null)
        {
            // A drop with no game configured must say why, not swallow it.
            if (paths.Count > 0) Message = "Set your SCUM folder in Settings to manage mods.";
            return System.Threading.Tasks.Task.CompletedTask;
        }

        if (paths.Count == 0) return System.Threading.Tasks.Task.CompletedTask;

        // Copying mods can be large; do it off the window's thread and report when it's done.
        return ImportDroppedCoreAsync(paths);
    }

    /// <summary>A plain line saying what a pack holds — and what it never will.</summary>
    public string PackNote =>
        "Packs the mods SCMW — Lone Wolf made into one ZIP you can keep as a backup or pass on. " +
        "Switched-off mods come too — you just switch them on again afterwards. Mods you added from " +
        "a file, or that another tool keeps, are never included; the ZIP says which they were and why.";

    /// <summary>Packs the mods SCMW made into <paramref name="zipPath"/>, then reports plainly.</summary>
    public async System.Threading.Tasks.Task PackModsToAsync(string zipPath)
    {
        if (_service is null)
        {
            Message = "Set your SCUM folder in Settings to manage mods.";
            return;
        }

        try
        {
            var outcome = await System.Threading.Tasks.Task.Run(() => _service.PackOurMods(zipPath));
            if (outcome.Ok)
            {
                Message = "ZIP has been created. All good!";
                IsMessageGood = true;                     // set after: every message starts as not-good
                CanRestartAsAdmin = false;
                return;
            }

            Message = outcome.Error ?? "That pack could not be written.";
            CanRestartAsAdmin = false;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = "That pack could not be written. Check the folder and try again.";
        }
    }

    private async System.Threading.Tasks.Task ImportDroppedCoreAsync(IReadOnlyList<string> paths)
    {
        try
        {
            var outcome = await System.Threading.Tasks.Task.Run(() => _service!.Import(paths));
            Message = DescribeImport(outcome);
            CanRestartAsAdmin = NeedsAdmin(outcome.Failure);
            LoadFromService();
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Import failed unexpectedly ({ex.GetType().Name}).");
            Message = "That mod could not be added.";
        }
    }

    /// <summary>A plain line naming what was added, and exactly why anything was skipped.</summary>
    private static string DescribeImport(ImportOutcome outcome)
    {
        var duplicates = outcome.Skipped - outcome.SkippedNotPak;
        var reasons = new List<string>();
        if (duplicates > 0) reasons.Add($"{duplicates} already installed");
        if (outcome.SkippedNotPak > 0) reasons.Add($"{outcome.SkippedNotPak} not a .pak");

        if (outcome.Imported > 0)
        {
            var line = $"Added {outcome.Imported} mod{(outcome.Imported == 1 ? "" : "s")}.";
            return reasons.Count > 0 ? line + $" Skipped: {string.Join(", ", reasons)}." : line;
        }

        return reasons.Count > 0
            ? $"Nothing added — {string.Join(", ", reasons)}."
            : (outcome.Error ?? "That mod could not be added.");
    }

    [RelayCommand]
    private void RequestRemove(InstalledMod? mod)
    {
        if (mod is not null) mod.IsConfirmingRemove = true;
    }

    [RelayCommand]
    private void CancelRemove(InstalledMod? mod)
    {
        if (mod is not null) mod.IsConfirmingRemove = false;
    }

    [RelayCommand]
    private void ConfirmRemove(InstalledMod? mod)
    {
        if (mod is null) return;

        if (_service is null)
        {
            _mods.Remove(mod);
            RaiseAll();
            return;
        }

        var result = _service.Remove(mod.FileName!);
        Message = result.Ok ? "" : (result.Error ?? "That mod could not be removed.");
        CanRestartAsAdmin = !result.Ok && NeedsAdmin(result.Failure);
        LoadFromService();
    }

    [RelayCommand]
    private void AddMod()
    {
        if (_service is not null) return;                      // the real Add uses the file picker
        _added++;
        _mods.Add(_added == 1 ? "ExternalMod.pak" : $"ExternalMod {_added}.pak");
    }

    [RelayCommand]
    private void GoToBuild() => _nav.GoTo(ScreenKey.Build);

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Mods));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
