using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;

namespace SCMW.App.Tests;

/// <summary>
/// Renders a window to a PNG under &lt;repo&gt;/artifacts/screenshots so the look can be
/// reviewed as a picture instead of described in words.
/// </summary>
public static class Screenshot
{
    public static string Capture(Window window, string fileName)
        => Capture(window, fileName, "screenshots");

    /// <summary>Renders a window to a PNG under &lt;repo&gt;/artifacts/&lt;subfolder&gt;.</summary>
    public static string Capture(Window window, string fileName, string subfolder)
    {
        var dir = Path.Combine(FindRepoRoot(), "artifacts", subfolder);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, fileName + ".png");
        var frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("capture returned no frame");

        frame.Save(path);
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SCMW.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
