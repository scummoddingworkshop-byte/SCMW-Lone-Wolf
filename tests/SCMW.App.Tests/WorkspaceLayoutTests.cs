using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SCMW.App.Views;
using SCMW.Core.Settings;

namespace SCMW.App.Tests;

/// <summary>
/// The Workspace's list column can be dragged wider or narrower, and the app remembers the width
/// the player chose. The number is kept between a floor and a cap so the page can never be
/// dragged into something unusable.
/// </summary>
public class WorkspaceLayoutTests
{
    [Fact]
    public void The_list_starts_at_the_default_width()
    {
        WorkspaceLayout.Restore(null);

        Assert.Equal(340, WorkspaceLayout.SavedWidth);
    }

    [Fact]
    public void A_saved_width_is_kept_between_the_floor_and_the_cap()
    {
        WorkspaceLayout.Restore(120);
        Assert.Equal(260, WorkspaceLayout.SavedWidth);     // too narrow: the floor

        WorkspaceLayout.Restore(9000);
        Assert.Equal(520, WorkspaceLayout.SavedWidth);     // too wide: the cap

        WorkspaceLayout.Restore(400);
        Assert.Equal(400, WorkspaceLayout.SavedWidth);     // in range: kept as it is
    }

    [Fact]
    public void Remembering_a_width_tells_the_app_once()
    {
        WorkspaceLayout.Restore(null);

        var told = new List<double>();
        void OnWidthChanged(double width) => told.Add(width);

        WorkspaceLayout.WidthChanged += OnWidthChanged;
        try
        {
            WorkspaceLayout.Remember(400);
            WorkspaceLayout.Remember(400.2);   // the same width again: nothing new to save
        }
        finally
        {
            WorkspaceLayout.WidthChanged -= OnWidthChanged;
        }

        Assert.Equal(new[] { 400d }, told);
    }

    [Fact]
    public void The_chosen_width_survives_a_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-list-width-" + Guid.NewGuid().ToString("N"));
        try
        {
            new JsonSettingsStore(dir).Save(new AppSettings(null, null, WorkspaceListWidth: 400));

            Assert.Equal(400, new JsonSettingsStore(dir).Load().WorkspaceListWidth);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* a temp folder left behind is harmless */ }
        }
    }

    [AvaloniaFact]
    public void Dragging_the_divider_moves_the_list_wider()
    {
        WorkspaceLayout.Restore(null);

        var view = new WorkspaceView { DataContext = new WorkspaceViewModel() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        window.UpdateLayout();

        var columns = view.FindControl<Grid>("WorkspaceColumns")!;
        var before = columns.ColumnDefinitions[0].Width.Value;

        var splitter = view.FindControl<GridSplitter>("ListSplitter")!;
        var grab = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, 100), window)!.Value;

        window.MouseDown(grab, MouseButton.Left);
        window.MouseMove(grab + new Point(60, 0));
        window.MouseUp(grab + new Point(60, 0), MouseButton.Left);
        window.UpdateLayout();

        // Dragged 60 pixels right: the list should be 60 pixels wider, so the main area gets less.
        Assert.Equal(before + 60, columns.ColumnDefinitions[0].Width.Value, 1);
    }

    [AvaloniaFact]
    public void The_page_has_a_divider_that_can_be_dragged()
    {
        var view = new WorkspaceView { DataContext = new WorkspaceViewModel() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        window.UpdateLayout();

        var grid = view.FindControl<Grid>("WorkspaceColumns");
        Assert.NotNull(grid);
        Assert.Equal(WorkspaceLayout.SavedWidth, grid!.ColumnDefinitions[0].Width.Value);
        Assert.Equal(WorkspaceLayout.MinWidth, grid.ColumnDefinitions[0].MinWidth);
        Assert.Equal(WorkspaceLayout.MaxWidth, grid.ColumnDefinitions[0].MaxWidth);

        // A visible line sits in the gap, so the two areas read as separate.
        var splitter = view.FindControl<GridSplitter>("ListSplitter");
        Assert.NotNull(splitter);
        var divider = view.FindControl<Border>("ListDivider");
        Assert.NotNull(divider);
        Assert.Equal(1, divider!.Width);
        Assert.NotNull(divider.Background);
    }
}
