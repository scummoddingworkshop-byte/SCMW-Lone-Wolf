using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCMW.App;

/// <summary>
/// The friendly front door: "What would you like to change?" Each tile opens that kind of
/// content — the kind's own screen until Phase 2's Workspace takes over.
/// </summary>
public partial class ChooserViewModel : ObservableObject
{
    private readonly INavigation _nav;

    public ChooserViewModel(INavigation? nav = null) => _nav = nav ?? NavigationHost.Shared;

    [RelayCommand] private void OpenItems() => _nav.GoTo(ScreenKey.Items);

    [RelayCommand] private void OpenBaseBuilding() => _nav.GoTo(ScreenKey.BaseBuilding);

    [RelayCommand] private void OpenRecipes() => _nav.GoTo(ScreenKey.Recipes);
}
