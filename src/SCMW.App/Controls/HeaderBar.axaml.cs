using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Controls;

/// <summary>
/// The top bar. Brand on the left, the current project in the middle, SCUM status and
/// utility icons on the right. All text comes from the ShellViewModel via bindings.
/// </summary>
public partial class HeaderBar : UserControl
{
    public HeaderBar() => AvaloniaXamlLoader.Load(this);
}
