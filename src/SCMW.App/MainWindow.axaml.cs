using System;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SCMW.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The taskbar/window icon. The exe icon is set in the project file; this makes the running
        // window match it. Cosmetic only — never let a missing resource stop the app opening.
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SCMW-Lone-Wolf/Assets/scmw.png")));
        }
        catch
        {
            // no icon is fine
        }
    }

    /// <summary>
    /// The rail shows exactly one lit row. A MAKE row (and a "coming soon" idea) is a command, not a
    /// place: the shell moves the highlight back to the row that owns the open screen. A ListBox keeps
    /// the row the mouse picked selected even after that, so re-apply the shell's choice once the click
    /// has finished — otherwise two rows look lit and the same row can't be clicked again.
    /// </summary>
    private void OnRailSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (list.DataContext is not NavBandSection band) return;
            if (ReferenceEquals(list.SelectedItem, band.Selected)) return;
            list.SelectedItem = band.Selected;
        });
    }
}
