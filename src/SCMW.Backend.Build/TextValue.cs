using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>
/// Sets the words a game text shows. A cooked text comes in two shapes we meet on items:
/// <list type="bullet">
/// <item><b>Base</b> — the words are the text's own source string (the name). The game still resolves
/// them through the text's KEY (editing only the source changes nothing, tested 2026-10-16), so the
/// words are written under a fresh key.</item>
/// <item><b>String-table entry</b> — the words are looked up in a game table (the description), so
/// changing the stored key would still show the old words. We turn it into a plain Base text with our
/// own key and source, which the game shows directly.</item>
/// </list>
/// Richer shapes (texts built from arguments) are refused plainly rather than flattened by guesswork.
/// </summary>
internal static class TextValue
{
    internal static bool TrySet(TextPropertyData text, string raw, out string? error)
    {
        error = null;

        var words = (raw ?? string.Empty).Trim();
        if (words.Length == 0)
        {
            error = "Enter the words to show.";
            return false;
        }

        try
        {
            switch (text.HistoryType)
            {
                case TextHistoryType.Base:
                    // The game resolves the shown words through the text's KEY, not the source string:
                    // editing only the source (same key) changes nothing in game (tested 2026-10-16).
                    // A fresh key with our words is the shape that shows (proven for descriptions and
                    // cooking titles), so always issue one when the words are set here.
                    text.Namespace = FString.FromString(string.Empty);
                    text.Value = FString.FromString(NewKey());
                    text.CultureInvariantString = FString.FromString(words);
                    return true;

                case TextHistoryType.None:
                case TextHistoryType.RawText:
                case TextHistoryType.StringTableEntry:
                    // Stop looking the words up in the game's table: make this text stand alone.
                    text.HistoryType = TextHistoryType.Base;
                    text.TableId = default;
                    text.Namespace = FString.FromString(string.Empty);
                    text.Value = FString.FromString(NewKey());
                    text.CultureInvariantString = FString.FromString(words);
                    return true;

                default:
                    error = "That text is built from other values, so it cannot be reworded here yet.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Setting a text value failed ({ex.GetType().Name}).");
            error = "That text couldn't be set.";
            return false;
        }
    }

    /// <summary>A fresh 32-character key, shaped like the game's own text keys.</summary>
    private static string NewKey() => Guid.NewGuid().ToString("N").ToUpperInvariant();
}
