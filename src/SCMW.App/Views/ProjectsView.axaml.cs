using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace SCMW.App.Views;

/// <summary>
/// The Projects screen: a searchable grid of project tiles, an inline create/rename
/// panel, and honest empty / no-matches states. Reads everything from its view model.
/// </summary>
public partial class ProjectsView : UserControl
{
    public ProjectsView() => AvaloniaXamlLoader.Load(this);

    private ProjectsViewModel? Vm => DataContext as ProjectsViewModel;

    /// <summary>Clicking a change card opens that item in the Modify screen. A click on one of the
    /// card's own buttons (its Undo) is left to the button.</summary>
    private void OnChangeCardReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (sender is Control { DataContext: ProjectChangeGroup group })
        {
            Vm?.OpenItemCommand.Execute(group);
            e.Handled = true;
        }
    }

    /// <summary>Opens the file picker so the player can choose a picture for the panel.</summary>
    private async void OnChoosePicture(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a project picture",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Picture") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } },
            },
        });

        var path = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(path)) Vm?.SetDraftIcon(path!);
    }
}
