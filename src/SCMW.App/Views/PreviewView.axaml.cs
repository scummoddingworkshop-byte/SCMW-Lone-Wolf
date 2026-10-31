using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace SCMW.App.Views;

/// <summary>
/// The Preview screen: exactly which files the chosen project will change, each field shown as
/// before → after, so the player sees the diff before building. Reads everything from its view model.
/// </summary>
public partial class PreviewView : UserControl
{
    public PreviewView() => AvaloniaXamlLoader.Load(this);

    /// <summary>Clicking a card opens that item in the Modify screen. A click on one of the card's
    /// own buttons (its Remove) is left to the button.</summary>
    private void OnChangeCardReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (sender is Control { DataContext: BuildChange row } && DataContext is PreviewViewModel vm)
        {
            vm.OpenItemCommand.Execute(row);
            e.Handled = true;
        }
    }
}
