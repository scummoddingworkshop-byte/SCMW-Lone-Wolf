using System.IO;
using SCMW.Backend.Pak;
using SCMW.Core.Content;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App;

/// <summary>
/// Builds the one content source from the player's settings: the game's Content\Paks folder
/// plus the saved AES key, indexed and icon-cached inside the workspace. With no game path or
/// no workspace it returns a source left NotConfigured — never throws, never guesses a path.
/// </summary>
public static class ContentSourceFactory
{
    /// <summary>The game's Content\Paks folder when the game path is set and that folder exists; else null.</summary>
    public static string? PakFolderFor(AppSettings settings, WorkspacePaths? workspace)
    {
        if (string.IsNullOrWhiteSpace(settings.GamePath)) return null;

        var paks = Path.Combine(settings.GamePath!, "SCUM", "Content", "Paks");
        return Directory.Exists(paks) ? paks : null;
    }

    /// <summary>A configured, index-backed source; NotConfigured when the game or workspace is missing.</summary>
    public static IContentSource Create(AppSettings settings, WorkspacePaths? workspace)
    {
        var paks = PakFolderFor(settings, workspace);
        var cache = workspace?.Cache;

        var source = cache is null
            ? new PakContentSource()
            : new PakContentSource(
                new PakMount(),
                new AssetIndex(),
                Path.Combine(cache, "assets.db"),
                new FileIconCache(Path.Combine(cache, "icons")));

        source.Configure(paks ?? "", settings.AesKey, settings.BuildId);
        return source;
    }
}
