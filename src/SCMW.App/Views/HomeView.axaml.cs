using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Home dashboard: hero band, welcome card, four quick actions, and recent
/// projects (with an empty state). It reads everything from its HomeViewModel.
/// </summary>
public partial class HomeView : UserControl
{
    public HomeView() => AvaloniaXamlLoader.Load(this);
}
