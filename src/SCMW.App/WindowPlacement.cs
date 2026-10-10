using System;
using Avalonia;
using Avalonia.Controls;
using SCMW.Core.Settings;

namespace SCMW.App;

/// <summary>
/// Remembers where the main window was and how big it was, so it reopens the same shape in the
/// same spot. Two plain safety rules: the size is clamped to something usable, and a saved spot
/// on a monitor that no longer exists is ignored (the window recentres rather than opening
/// off-screen).
/// </summary>
public static class WindowPlacement
{
    /// <summary>The size used until the player resizes the window for the first time.</summary>
    public const double DefaultWidth = 1110;
    public const double DefaultHeight = 860;

    /// <summary>Never open smaller than this, or the layout crushes.</summary>
    public const double MinWidth = 600;
    public const double MinHeight = 480;

    /// <summary>Never open bigger than this (a guard against a corrupt saved size).</summary>
    public const double MaxWidth = 6000;
    public const double MaxHeight = 4000;

    /// <summary>Puts the saved size and position onto the window, or a sensible default.</summary>
    public static void Apply(Window window, AppSettings settings)
    {
        window.MinWidth = MinWidth;
        window.MinHeight = MinHeight;
        window.Width = Size(settings.WindowWidth, DefaultWidth, MinWidth, MaxWidth);
        window.Height = Size(settings.WindowHeight, DefaultHeight, MinHeight, MaxHeight);

        if (settings.WindowX is { } x && settings.WindowY is { } y)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(x, y);
            window.Opened += (_, _) => NudgeOnScreen(window);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (settings.WindowMaximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>Records the window's current shape into the settings and saves them.</summary>
    public static void Save(Window window, ISettingsStore settings)
    {
        try
        {
            settings.Save(Capture(settings.Load(), window.WindowState, window.Position, window.ClientSize));
        }
        catch
        {
            // Remembering the window shape must never get in the way of closing the app.
        }
    }

    /// <summary>The settings with the window's current shape folded in (latest normal bounds only).</summary>
    public static AppSettings Capture(AppSettings current, WindowState state, PixelPoint position, Size clientSize)
    {
        // Only record bounds while the window is normal: a maximised/full-screen size is the
        // screen's, not the player's choice, so we keep the last normal bounds in that case.
        var normal = state == WindowState.Normal
                     && clientSize.Width >= MinWidth && clientSize.Height >= MinHeight;

        return current with
        {
            WindowMaximized = state == WindowState.Maximized,
            WindowX = normal ? position.X : current.WindowX,
            WindowY = normal ? position.Y : current.WindowY,
            WindowWidth = normal ? (int)clientSize.Width : current.WindowWidth,
            WindowHeight = normal ? (int)clientSize.Height : current.WindowHeight,
        };
    }

    private static double Size(int? saved, double fallback, double min, double max)
        => saved is > 0 ? Math.Clamp(saved.Value, min, max) : fallback;

    /// <summary>If the saved spot is off every screen (a monitor was unplugged), move it into view.</summary>
    private static void NudgeOnScreen(Window window)
    {
        try
        {
            var area = window.Screens.ScreenFromWindow(window)?.WorkingArea
                       ?? window.Screens.Primary?.WorkingArea;
            if (area is not { } screen) return;

            var position = window.Position;
            var size = window.ClientSize;
            var rect = new PixelRect(position.X, position.Y, (int)size.Width, (int)size.Height);
            if (!screen.Intersects(rect))
                window.Position = new PixelPoint(screen.X + 40, screen.Y + 40);
        }
        catch
        {
            // A platform that cannot tell us about screens simply keeps the saved spot.
        }
    }
}
