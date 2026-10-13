using System;
using System.IO;

namespace SCMW.App;

/// <summary>The outcome of turning SCUM's startup videos on or off; one plain reason if it failed.</summary>
public sealed record IntroVideoResult(bool Ok, string Message);

/// <summary>Turns SCUM's startup intro videos on or off by renaming the clip files. Abstracted so
/// screens and tests never touch the game folder.</summary>
public interface IIntroVideos
{
    /// <summary>Disables the startup clips when <paramref name="skip"/> is true, restores them when false.</summary>
    IntroVideoResult Apply(string? gamePath, bool skip);
}

/// <summary>
/// The real one. Disabling renames each startup clip to <c>&lt;name&gt;.bk2.off</c> so the game can't
/// find it; restoring renames it back. The two in-game cinematics (character creation, tutorial) are
/// deliberately left alone. Safe to repeat: extra calls change nothing.
/// </summary>
public sealed class SystemIntroVideos : IIntroVideos
{
    private static readonly string[] Clips =
    {
        "SplashVideoGamepires.bk2",
        "SplashVideoTechnologies.bk2",
        "IntroCinematic.bk2",
    };

    private const string OffSuffix = ".off";
    private const string CannotChange =
        "SCUM's video files couldn't be changed. If it keeps happening, try running SCMW — Lone Wolf as administrator.";

    public IntroVideoResult Apply(string? gamePath, bool skip)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return new(false, "Set your SCUM folder first.");

        var movies = Path.Combine(gamePath, "SCUM", "Content", "Movies");
        if (!Directory.Exists(movies))
            return new(false, "SCUM's video folder wasn't found.");

        try
        {
            foreach (var clip in Clips)
            {
                var full = Path.Combine(movies, clip);
                var off = full + OffSuffix;

                if (skip)
                {
                    // Disable: hide the clip. If a game update dropped a fresh .bk2 beside our old
                    // .off, replace our stale copy with the fresh one.
                    if (File.Exists(full))
                    {
                        if (File.Exists(off)) File.Delete(off);
                        File.Move(full, off);
                    }
                }
                else if (File.Exists(off))
                {
                    // Restore: put the clip back, unless the game already has its own (then just
                    // drop our stale copy).
                    if (File.Exists(full)) File.Delete(off);
                    else File.Move(off, full);
                }
            }
        }
        catch (IOException)
        {
            return new(false, CannotChange);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, CannotChange);
        }

        return new(true, skip ? "Intro videos skipped." : "Intro videos play as normal.");
    }
}
