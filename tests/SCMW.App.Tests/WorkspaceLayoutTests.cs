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
    public void A_width_that_is_not_a_real_number_is_never_saved()
    {
        WorkspaceLayout.Restore(null);   // 340

        // A column that has not been laid out can report infinity; the cap would swallow it as 520.
        WorkspaceLayout.Remember(double.PositiveInfinity);
        Assert.Equal(WorkspaceLayout.DefaultWidth, WorkspaceLayout.SavedWidth);

        WorkspaceLayout.Remember(double.NaN);
        Assert.Equal(WorkspaceLayout.DefaultWidth, WorkspaceLayout.SavedWidth);
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
    public void A_drag_reports_the_width_it_was_left_at()
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

        // No layout pass between the release and the read: exactly what a real drag does.
        window.MouseDown(grab, MouseButton.Left);
        window.MouseMove(grab + new Point(60, 0));
        window.MouseUp(grab + new Point(60, 0), MouseButton.Left);

        Assert.Equal(before + 60, view.ListWidth, 1);
    }

    [AvaloniaFact]
    public void Dragging_twice_remembers_the_last_width()
    {
        WorkspaceLayout.Restore(null);

        var view = new WorkspaceView { DataContext = new WorkspaceViewModel() };
        var window = new Window { Content = view, Width = 2560, Height = 800 };
        window.Show();
        window.UpdateLayout();

        var columns = view.FindControl<Grid>("WorkspaceColumns")!;
        var start = columns.ColumnDefinitions[0].Width.Value;
        var splitter = view.FindControl<GridSplitter>("ListSplitter")!;

        void DragBy(double dx)
        {
            var grab = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, 100), window)!.Value;
            window.MouseDown(grab, MouseButton.Left);
            window.MouseMove(grab + new Point(dx, 0));
            window.MouseUp(grab + new Point(dx, 0), MouseButton.Left);
            window.UpdateLayout();
        }

        DragBy(80);     // wider
        Assert.Equal(start + 80, columns.ColumnDefinitions[0].Width.Value, 1);

        DragBy(-80);    // and back
        Assert.Equal(start, columns.ColumnDefinitions[0].Width.Value, 1);
        Assert.Equal(start, WorkspaceLayout.SavedWidth, 1);
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

        // What is remembered is the width the player actually left it at.
        Assert.Equal(columns.ColumnDefinitions[0].Width.Value, WorkspaceLayout.SavedWidth, 1);
    }

    [AvaloniaFact]
    public void The_page_has_a_divider_that_can_be_dragged()
    {
        WorkspaceLayout.Restore(420);   // the width a player left behind last time

        var view = new WorkspaceView { DataContext = new WorkspaceViewModel() };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        window.UpdateLayout();

        var grid = view.FindControl<Grid>("WorkspaceColumns");
        Assert.NotNull(grid);
        Assert.Equal(420, grid!.ColumnDefinitions[0].Width.Value);   // it opens at the saved width
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
