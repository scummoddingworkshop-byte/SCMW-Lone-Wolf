using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCMW.App;

/// <summary>
/// The friendly front door: "What would you like to change?" Each tile opens the Workspace with
/// that kind of content already picked (Task 3.0). The "New recipes &amp; objects" tile starts a
/// brand-new recipe (Task 7.5).
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

    [RelayCommand] private void OpenItems() => OpenKind("Items");

    [RelayCommand] private void OpenBaseBuilding() => OpenKind("Base building");

    [RelayCommand] private void OpenRecipes() => OpenKind("Recipes");

    /// <summary>The "New recipes &amp; objects" tile: open the Workspace on Recipes and start a new
    /// recipe (Task 7.5's front-door start point).</summary>
    [RelayCommand] private void NewContent()
    {
        if (_workspace is not null)
        {
            _ = _workspace.OpenNewRecipeAsync();
        }

        _nav.GoTo(ScreenKey.Workspace);
    }

    /// <summary>A tile opens the Workspace with that kind already picked (Task 3.0).</summary>
    private void OpenKind(string kind)
    {
        if (_workspace is not null)
        {
            _ = _workspace.OpenKindAsync(kind);
        }

        _nav.GoTo(ScreenKey.Workspace);
    }
}
