namespace SCMW.App;

/// <summary>
/// Where a player can find SCUM's shared unlock key. A public key list is the main route (no download,
/// no sign-up); the FModel website is the fallback — its "Join Our Discord" leads to the server where
/// the current key is posted.
/// </summary>
public static class KeySources
{
    /// <summary>A public page listing Unreal Engine AES keys — includes SCUM. No login or download.</summary>
    public const string PublicList = "https://gist.github.com/pixelkat5/cffd710913bc309c8e17f819f8b4bc37";

    /// <summary>The official FModel site; its Discord link leads to the channel with the current key.</summary>
    public const string Fallback = "https://fmodel.app";
}
