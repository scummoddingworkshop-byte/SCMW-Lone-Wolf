using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Backups screen: removed mods and deleted projects SCMW — Lone Wolf keeps in the workspace, with
/// one-click restore and delete. Reads everything from its view model.
/// </summary>
public partial class BackupsView : UserControl
{
    public BackupsView() => AvaloniaXamlLoader.Load(this);
}
