using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Build screen: what's in the mod, one big Build button, the step bar, the outcome in
/// plain words, and the Advanced expander. Reads everything from its view model.
/// </summary>
public partial class BuildView : UserControl
{
    public BuildView()
    {
        AvaloniaXamlLoader.Load(this);

        // Rename only when the player finishes editing (Enter or leaving the box), not per keystroke.
        if (this.FindControl<TextBox>("RenameBox") is { } box)
        {
            box.LostFocus += (_, _) => CommitRename();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) CommitRename();
            };
        }
    }

    private void CommitRename()
    {
        if (DataContext is BuildViewModel vm) vm.CommitRenameCommand.Execute(null);
    }
}
