using Avalonia.Headless.XUnit;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App.Tests;

public class ContentSourceFactoryTests
{
    [AvaloniaFact]
    public void Pak_folder_is_found_under_the_game_path()
    {
        var game = Path.Combine(Path.GetTempPath(), "scmw-fake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(game, "SCUM", "Content", "Paks"));
        try
        {
            var folder = ContentSourceFactory.PakFolderFor(
                new AppSettings(null, game), workspace: null);

            Assert.Equal(Path.Combine(game, "SCUM", "Content", "Paks"), folder);
        }
        finally { Directory.Delete(game, true); }
    }

    [AvaloniaFact]
    public void No_game_path_means_no_pak_folder()
    {
        Assert.Null(ContentSourceFactory.PakFolderFor(new AppSettings(null, null), null));
    }

    [AvaloniaFact]
    public void A_game_path_with_no_paks_folder_is_not_a_pak_folder()
    {
        var game = Path.Combine(Path.GetTempPath(), "scmw-fake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(game);
        try
        {
            Assert.Null(ContentSourceFactory.PakFolderFor(new AppSettings(null, game), null));
        }
        finally { Directory.Delete(game, true); }
    }

    [AvaloniaFact]
    public void With_no_game_configured_the_source_is_not_configured_and_does_not_throw()
    {
        var source = ContentSourceFactory.Create(new AppSettings(null, null), null);

        Assert.Equal(SCMW.Core.Content.ContentSourceState.NotConfigured, source.Status.State);
    }
}
