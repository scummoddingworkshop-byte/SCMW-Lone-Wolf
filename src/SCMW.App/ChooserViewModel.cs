using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCMW.App;

/// <summary>
/// The friendly front door: "What would you like to change?" Each tile opens the Workspace with
/// that kind of content already picked (Task 3.0).
/// </summary>
public partial class ChooserViewModel : ObservableObject
{
    private readonly INavigation _nav;
    private readonly WorkspaceViewModel? _workspace;

    public ChooserViewModel(INavigation? nav = null, WorkspaceViewModel? workspace = null)
    {
        _nav = nav ?? NavigationHost.Shared;
        _workspace = workspace;
    }

    /// <summary>The red "on the way" message shown when a not-yet-built kind is clicked.</summary>
    public string OnTheWayText => OnTheWay.Message;

    /// <summary>Whether the "on the way" message is showing (a locked tile was clicked).</summary>
    [ObservableProperty] private bool _showOnTheWayMessage;

    [RelayCommand] private void OpenItems() => OpenKind("Items");

    [RelayCommand] private void OpenBaseBuilding() => OpenKind("Base building");

    [RelayCommand] private void OpenRecipes() => OpenKind("Recipes");

    /// <summary>A tile opens the Workspace with that kind already picked (Task 3.0).</summary>
    private void OpenKind(string kind)
    {
        if (_workspace is not null)
        {
            _ = _workspace.OpenKindAsync(kind);
        }

        _nav.GoTo(ScreenKey.Workspace);
    }

    /// <summary>A kind we have not built yet (levels 3–5): say so, rather than doing nothing.</summary>
    [RelayCommand] private void LockedKind() => ShowOnTheWayMessage = true;
}
