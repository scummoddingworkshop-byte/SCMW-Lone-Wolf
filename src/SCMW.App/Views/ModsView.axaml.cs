using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace SCMW.App.Views;

/// <summary>
/// The Mods screen: the installed list, the summary, the drop zone and the empty state.
/// Reads everything from its view model; the drop zone and the Add button feed it dropped/chosen
/// <c>.pak</c> files.
/// </summary>
public partial class ModsView : UserControl
{
    public ModsView()
    {
        AvaloniaXamlLoader.Load(this);

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);

        if (this.FindControl<Button>("AddModButton") is { } add)
            add.Click += async (_, _) => await PickAndImportAsync();
    }

    private ModsViewModel? Vm => DataContext as ModsViewModel;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        // CS0618: DragEventArgs.Data is marked obsolete in favour of the async DataTransfer API,
        // but it is the simple, synchronous way to read dropped files and is exactly what this
        // one-line handler needs. Suppressed narrowly here.
#pragma warning disable CS0618
        var paths = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
#pragma warning restore CS0618

        if (paths is { Count: > 0 }) _ = Vm?.ImportDropped(paths);
    }

    private async Task PickAndImportAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add a mod",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("SCUM mod") { Patterns = new[] { "*.pak" } } },
        });

        var paths = files.Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        if (paths.Count > 0 && Vm is { } vm) await vm.ImportDropped(paths);
    }
}
