using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Build screen: "What's in this mod" (the preview/diff), the mod version and one big Build
/// button, the step bar, and the outcome in plain words. Reads everything from its view model.
/// </summary>
public partial class BuildView : UserControl
{
    public BuildView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
