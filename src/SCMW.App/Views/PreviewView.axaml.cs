using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Preview screen: exactly which files the chosen project will change, each field shown as
/// before → after, so the player sees the diff before building. Reads everything from its view model.
/// </summary>
public partial class PreviewView : UserControl
{
    public PreviewView() => AvaloniaXamlLoader.Load(this);
}
