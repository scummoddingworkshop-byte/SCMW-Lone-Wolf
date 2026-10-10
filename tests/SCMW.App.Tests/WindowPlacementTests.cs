using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SCMW.Core.Settings;

namespace SCMW.App.Tests;

public class WindowPlacementTests
{
    private static AppSettings Empty() => new(null, null);

    [Fact]
    public void The_default_window_width_is_1110() =>
        Assert.Equal(1110, WindowPlacement.DefaultWidth);

    [AvaloniaFact]
    public void First_run_uses_the_compact_default_size_and_centres()
    {
        var window = new MainWindow();

        WindowPlacement.Apply(window, Empty());

        Assert.Equal(WindowPlacement.DefaultWidth, window.Width);
        Assert.Equal(WindowPlacement.DefaultHeight, window.Height);
        Assert.Equal(WindowPlacement.MinWidth, window.MinWidth);
        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
    }

    [AvaloniaFact]
    public void A_saved_size_and_position_are_restored()
    {
        var window = new MainWindow();
        var saved = Empty() with { WindowX = 120, WindowY = 80, WindowWidth = 900, WindowHeight = 640 };

        WindowPlacement.Apply(window, saved);

        Assert.Equal(900, window.Width);
        Assert.Equal(640, window.Height);
        Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
        Assert.Equal(new PixelPoint(120, 80), window.Position);
    }

    [AvaloniaFact]
    public void A_too_small_or_silly_saved_size_is_clamped()
    {
        var window = new MainWindow();

        WindowPlacement.Apply(window, Empty() with { WindowWidth = 10, WindowHeight = 999999 });

        Assert.Equal(WindowPlacement.MinWidth, window.Width);
        Assert.Equal(WindowPlacement.MaxHeight, window.Height);
    }

    [AvaloniaFact]
    public void A_maximised_window_is_remembered_as_maximised()
    {
        var window = new MainWindow();

        WindowPlacement.Apply(window, Empty() with { WindowMaximized = true });

        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [AvaloniaFact]
    public void Capture_records_the_normal_bounds()
    {
        var updated = WindowPlacement.Capture(Empty(), WindowState.Normal,
            new PixelPoint(50, 60), new Size(820, 600));

        Assert.Equal(50, updated.WindowX);
        Assert.Equal(60, updated.WindowY);
        Assert.Equal(820, updated.WindowWidth);
        Assert.Equal(600, updated.WindowHeight);
        Assert.False(updated.WindowMaximized);
    }

    [AvaloniaFact]
    public void Capture_while_maximised_keeps_the_last_normal_bounds()
    {
        var previous = Empty() with { WindowX = 10, WindowY = 20, WindowWidth = 700, WindowHeight = 720 };

        var updated = WindowPlacement.Capture(previous, WindowState.Maximized,
            new PixelPoint(0, 0), new Size(1920, 1080));

        Assert.True(updated.WindowMaximized);
        Assert.Equal(10, updated.WindowX);        // the normal spot is untouched
        Assert.Equal(700, updated.WindowWidth);
    }

    [AvaloniaFact]
    public void Saving_folds_the_window_shape_into_the_settings_store()
    {
        var store = new RecordingStore();
        var window = new MainWindow { Width = 860, Height = 610 };

        WindowPlacement.Save(window, store);

        Assert.NotNull(store.Saved);
        Assert.Equal((int)window.ClientSize.Width, store.Saved!.WindowWidth);
        Assert.Equal((int)window.ClientSize.Height, store.Saved.WindowHeight);
    }

    private sealed class RecordingStore : ISettingsStore
    {
        public AppSettings? Saved;
        public AppSettings Load() => new(null, null);
        public void Save(AppSettings s) => Saved = s;
    }
}
