using SCMW.App;

namespace SCMW.App.Tests;

public class IntroVideosTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-intros-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Movies => Path.Combine(_root, "SCUM", "Content", "Movies");

    private void MakeMovies(params string[] clips)
    {
        Directory.CreateDirectory(Movies);
        foreach (var clip in clips) File.WriteAllText(Path.Combine(Movies, clip), "video");
    }

    private static readonly string[] Startup =
    {
        "SplashVideoGamepires.bk2", "SplashVideoTechnologies.bk2", "IntroCinematic.bk2",
    };

    [Fact]
    public void Skipping_renames_the_startup_clips_so_they_cannot_play()
    {
        MakeMovies(Startup);

        var result = new SystemIntroVideos().Apply(_root, skip: true);

        Assert.True(result.Ok, result.Message);
        foreach (var clip in Startup)
        {
            Assert.False(File.Exists(Path.Combine(Movies, clip)));
            Assert.True(File.Exists(Path.Combine(Movies, clip + ".off")));
        }
    }

    [Fact]
    public void Restoring_puts_the_clips_back()
    {
        MakeMovies(Startup);
        var videos = new SystemIntroVideos();
        videos.Apply(_root, skip: true);

        var result = videos.Apply(_root, skip: false);

        Assert.True(result.Ok, result.Message);
        foreach (var clip in Startup)
        {
            Assert.True(File.Exists(Path.Combine(Movies, clip)));
            Assert.False(File.Exists(Path.Combine(Movies, clip + ".off")));
        }
    }

    [Fact]
    public void It_leaves_the_in_game_cinematics_alone()
    {
        MakeMovies("CharacterCreationCinematic.bk2", "FTUE_Cinematic.bk2");

        new SystemIntroVideos().Apply(_root, skip: true);

        Assert.True(File.Exists(Path.Combine(Movies, "CharacterCreationCinematic.bk2")));
        Assert.True(File.Exists(Path.Combine(Movies, "FTUE_Cinematic.bk2")));
    }

    [Fact]
    public void Skipping_twice_is_fine()
    {
        MakeMovies(Startup);
        var videos = new SystemIntroVideos();

        Assert.True(videos.Apply(_root, skip: true).Ok);
        Assert.True(videos.Apply(_root, skip: true).Ok);

        Assert.True(File.Exists(Path.Combine(Movies, "IntroCinematic.bk2.off")));
    }

    [Fact]
    public void A_game_update_that_brings_a_clip_back_leaves_the_original_on_restore()
    {
        // We disabled the clips, then a game update re-created the originals beside our .off copies.
        MakeMovies(Startup);
        var videos = new SystemIntroVideos();
        videos.Apply(_root, skip: true);
        MakeMovies(Startup);

        var result = videos.Apply(_root, skip: false);

        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Path.Combine(Movies, "IntroCinematic.bk2")));
        Assert.False(File.Exists(Path.Combine(Movies, "IntroCinematic.bk2.off")));
    }

    [Fact]
    public void A_restored_clip_is_disabled_again_cleanly()
    {
        // Both .bk2 and .off present (a game update put the .bk2 back). Skipping must still disable it.
        MakeMovies(Startup);
        var videos = new SystemIntroVideos();
        videos.Apply(_root, skip: true);
        MakeMovies(Startup);

        var result = videos.Apply(_root, skip: true);

        Assert.True(result.Ok, result.Message);
        Assert.False(File.Exists(Path.Combine(Movies, "IntroCinematic.bk2")));
        Assert.True(File.Exists(Path.Combine(Movies, "IntroCinematic.bk2.off")));
    }

    [Fact]
    public void With_no_game_folder_it_says_so_plainly()
    {
        var result = new SystemIntroVideos().Apply("", skip: true);

        Assert.False(result.Ok);
        Assert.Contains("SCUM folder", result.Message);
    }

    [Fact]
    public void With_no_video_folder_it_says_so_plainly()
    {
        Directory.CreateDirectory(_root);

        var result = new SystemIntroVideos().Apply(_root, skip: true);

        Assert.False(result.Ok);
        Assert.Contains("video folder", result.Message);
    }
}
