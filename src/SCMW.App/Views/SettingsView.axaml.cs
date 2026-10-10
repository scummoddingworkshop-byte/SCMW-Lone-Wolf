using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Settings screen: game folder, unlock key, workspace and storage, help level, About
/// and Support. Everything is shown in memory; nothing is read or written.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => AvaloniaXamlLoader.Load(this);
}
