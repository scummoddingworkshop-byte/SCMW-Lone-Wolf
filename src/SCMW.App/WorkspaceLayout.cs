using System;

namespace SCMW.App;

/// <summary>
/// How wide the Workspace's list column is: the default, the floor and the cap, and the width the
/// player last dragged it to. Remembered between launches like the window's own size and place.
/// </summary>
public static class WorkspaceLayout
{
    /// <summary>The width the list starts at, in pixels, before anyone drags the divider.</summary>
    public const double DefaultWidth = 340;

    /// <summary>The narrowest the list can be dragged.</summary>
    public const double MinWidth = 260;

    /// <summary>The widest the list can be dragged.</summary>
    public const double MaxWidth = 520;

    /// <summary>The width in use now.</summary>
    public static double SavedWidth { get; private set; } = DefaultWidth;

    /// <summary>Raised with the new width after the player drags the divider.</summary>
    public static event Action<double>? WidthChanged;

    /// <summary>Takes the saved width (or the default when there is none) and keeps it in range.</summary>
    public static void Restore(int? saved)
    {
        SavedWidth = Clamp(saved ?? DefaultWidth);
        SCMW.Core.Logging.Log.Info($"Workspace list width restored to {SavedWidth} (saved: {saved?.ToString() ?? "none"})");
    }

    /// <summary>Keeps a width between the floor and the cap.</summary>
    public static double Clamp(double width) =>
        double.IsNaN(width) ? DefaultWidth : Math.Clamp(width, MinWidth, MaxWidth);

    /// <summary>Records the width the player dragged to and tells whoever saves the settings.</summary>
    public static void Remember(double width)
    {
        var clamped = Clamp(width);
        if (Math.Abs(clamped - SavedWidth) < 0.5) return;   // the same width again: nothing to save

        SavedWidth = clamped;
        WidthChanged?.Invoke(clamped);
    }
}
