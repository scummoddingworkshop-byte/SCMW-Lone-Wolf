using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Workspace screen: shows the real fields of whichever file is open, each explained
/// (label, unit, risk dot, plain meaning). Reads everything from its view model.
/// </summary>
public partial class WorkspaceView : UserControl
{
    public WorkspaceView() => AvaloniaXamlLoader.Load(this);
}
