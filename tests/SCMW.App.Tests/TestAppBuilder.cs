using Avalonia;
using Avalonia.Headless;
using SCMW.App;

[assembly: AvaloniaTestApplication(typeof(SCMW.App.Tests.TestAppBuilder))]

namespace SCMW.App.Tests;

/// <summary>
/// Boots the Avalonia app off-screen so UI tests can run without a display.
/// Real drawing (UseHeadlessDrawing = false + Skia) is on so tests can capture
/// actual pixels to a PNG for visual review.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
