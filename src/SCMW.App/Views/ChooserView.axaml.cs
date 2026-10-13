using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>The friendly chooser: "What would you like to change?" Each tile opens a kind of content.</summary>
public partial class ChooserView : UserControl
{
    public ChooserView() => AvaloniaXamlLoader.Load(this);
}
