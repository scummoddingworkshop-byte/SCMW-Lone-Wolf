using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Controls;

/// <summary>
/// One field row of the Workspace. Its DataContext is the <see cref="WorkspaceField"/>.
/// </summary>
public partial class FieldRow : UserControl
{
    public FieldRow() => AvaloniaXamlLoader.Load(this);
}
