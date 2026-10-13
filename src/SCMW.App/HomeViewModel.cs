using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCMW.App;

/// <summary>
/// Home's brain: the recent-project list, the four quick actions, and the first-run welcome card.
/// The card nudges the player to set SCUM up first, and stops showing once the game is configured
/// (or the player dismisses it). It navigates through <see cref="INavigation"/> and knows nothing
/// about pixels.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly INavigation _nav;
    private readonly ProjectStore? _store;
    private readonly IReadOnlyList<ProjectSummary>? _samples;
    private readonly Func<bool>? _gameReady;
    private bool _welcomeDismissed;

    public HomeViewModel(INavigation? nav = null, IReadOnlyList<ProjectSummary>? projects = null,
                         ProjectStore? store = null, Func<bool>? gameReady = null)
    {
        _nav = nav ?? NavigationHost.Shared;
        _store = store;
        _samples = projects;
        _gameReady = gameReady;
        Chooser = new ChooserViewModel(_nav);

        if (_store is not null) _store.Changed += Refresh;
        Refresh();
        RefreshWelcome();
    }

    /// <summary>The most recently touched projects (up to four).</summary>
    public ObservableCollection<ProjectSummary> RecentProjects { get; } = new();

    /// <summary>True when there is at least one project to show.</summary>
    public bool HasProjects => RecentProjects.Count > 0;

    /// <summary>The chooser folded into Home — the friendly "what would you like to change?" tiles.</summary>
    public ChooserViewModel Chooser { get; }

    /// <summary>Whether the "set SCUM up first" card is showing.</summary>
    [ObservableProperty] private bool _showWelcome = true;

    /// <summary>
    /// Re-decides the welcome card: it nudges setup until the game is configured, and stays hidden
    /// once it is (or once the player has dismissed it this session).
    /// </summary>
    public void RefreshWelcome() =>
        ShowWelcome = !_welcomeDismissed && !(_gameReady?.Invoke() ?? false);

    [RelayCommand] private void NewProject() => _nav.GoTo(ScreenKey.Projects);

    [RelayCommand] private void OpenProject() => _nav.GoTo(ScreenKey.Projects);

    [RelayCommand] private void BrowseItems() => _nav.GoTo(ScreenKey.Items);

    [RelayCommand] private void BuildPlay() => _nav.GoTo(ScreenKey.Build);

    [RelayCommand] private void ViewAllProjects() => _nav.GoTo(ScreenKey.Projects);

    /// <summary>The first-run card sends the player to Settings (game folder + unlock key).</summary>
    [RelayCommand] private void StartWelcome() => _nav.GoTo(ScreenKey.Settings);

    [RelayCommand]
    private void DismissWelcome()
    {
        _welcomeDismissed = true;
        ShowWelcome = false;
    }

    /// <summary>Opens a card's project: it becomes current, then Items opens.</summary>
    [RelayCommand]
    private void OpenRecentProject(ProjectSummary? project)
    {
        if (project is not null && _store is not null && project.Id.Length > 0)
        {
            _store.SetCurrent(project.Id);
        }

        _nav.GoTo(ScreenKey.Items);
    }

    private void Refresh()
    {
        var currentId = _store?.Current?.Id;

        var rows = _store is null
            ? _samples ?? SampleProjects()
            : _store.All().Take(4).Select(r => ProjectSummary.From(r, r.Id == currentId)).ToList();

        RecentProjects.Clear();
        foreach (var row in rows) RecentProjects.Add(row);

        OnPropertyChanged(nameof(HasProjects));
    }

    private static IReadOnlyList<ProjectSummary> SampleProjects() => new[]
    {
        new ProjectSummary("Duct Tape Expanded",
            "Larger inventory size and increased stack amount", "2 hours ago"),
        new ProjectSummary("Military Backpack+",
            "Increased storage capacity and balanced weight", "1 day ago"),
        new ProjectSummary("Ammo Stack Adjustments",
            "Higher stack sizes for common ammunition types", "3 days ago"),
        new ProjectSummary("Vehicle Tweaks",
            "Adjusted storage and fuel capacity", "5 days ago"),
    };
}
