using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build;

/// <summary>Adds the project's own sections to a copy of the game's crafting menu data.</summary>
public interface ICraftingMenuWriter
{
    /// <summary>Adds every section to the menu data at <paramref name="uassetFilePath"/>, in place.</summary>
    WriteResult AddSections(string uassetFilePath, IReadOnlyList<NewBuildSection> sections);
}

/// <summary>
/// Writes a new section into SCUM's own crafting menu. The rail is a fixed list in the
/// <c>CraftingUIData</c> data asset — <c>ItemCategoryOrder</c> holds the shelf tags and
/// <c>CategoryNames</c> maps each tag to the words shown — so a section is added to both. The tag
/// itself also has to be registered in <c>DefaultGameplayTags.ini</c>; <see cref="AddTags"/> does the
/// text part. Proven in game on 2026-10-25.
/// </summary>
public sealed class CraftingMenuWriter : ICraftingMenuWriter
{
    /// <summary>The game's registered-tag list.</summary>
    public const string TagsIniPath = "SCUM/Config/DefaultGameplayTags.ini";

    /// <summary>The game's crafting menu data asset (the fixed shelf list and its names).</summary>
    public const string MenuDataPath = "SCUM/Content/ConZ_Files/UI/Crafting/CraftingUIData";

    /// <summary>The exact line shape the game's own tag list uses.</summary>
    public static string TagLine(string tag) =>
        $"+GameplayTagList=(Tag=\"{tag}\",DevComment=\"\")";

    /// <summary>
    /// Appends one registration line per tag that is not already registered, keeping every existing
    /// line exactly as it was. The lines go next to the game's own tag lines (never into another
    /// section), and a tag already registered — in any letter case, with any comment — is left alone.
    /// False with a plain reason when the file is not the game's tag list.
    /// </summary>
    public static bool TryAddTags(string? iniText, IEnumerable<string> tags,
                                  out string updated, out string? error)
    {
        updated = iniText ?? string.Empty;
        error = null;

        var newline = updated.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = updated.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var at = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].TrimStart().StartsWith("+GameplayTagList=", StringComparison.OrdinalIgnoreCase))
            {
                at = i;
                break;
            }
        }

        if (at < 0)
        {
            at = lines.FindIndex(l => l.Trim() == "[GameplayTags]");
            if (at < 0)
            {
                error = "The game's tag list was not in the shape we expected.";
                return false;
            }
        }

        var missing = tags
            .Where(t => !string.IsNullOrWhiteSpace(t) && !HasTag(lines, t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(TagLine)
            .ToList();

        if (missing.Count == 0) return true; // nothing to add: leave the file byte for byte

        lines.InsertRange(at + 1, missing);
        updated = string.Join(newline, lines);
        return true;
    }

    /// <summary>True when the file already registers this tag, whatever its case or comment.</summary>
    private static bool HasTag(IEnumerable<string> lines, string tag) =>
        lines.Any(l => l.TrimStart().StartsWith("+GameplayTagList=", StringComparison.OrdinalIgnoreCase)
                    && l.Contains($"Tag=\"{tag}\"", StringComparison.OrdinalIgnoreCase));

    public WriteResult AddSections(string uassetFilePath, IReadOnlyList<NewBuildSection> sections)
    {
        if (sections.Count == 0) return new WriteResult(true, null);

        UAsset asset;
        try
        {
            asset = new UAsset(uassetFilePath, EngineVersion.VER_UE4_27);
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Opening the crafting menu data failed ({ex.GetType().Name}).");
            return new WriteResult(false, "The game's crafting menu data could not be opened.");
        }

        try
        {
            var export = asset.Exports.OfType<NormalExport>()
                .FirstOrDefault(e => e.Data.Any(p => p.Name.ToString() == "ItemCategoryOrder"));
            if (export is null)
                return new WriteResult(false, "The game's crafting menu data was not in the shape we expected.");

            var order = export.Data.OfType<ArrayPropertyData>().Single(p => p.Name.ToString() == "ItemCategoryOrder");
            var names = export.Data.OfType<MapPropertyData>().Single(p => p.Name.ToString() == "CategoryNames");

            var orderTemplate = order.Value.OfType<StructPropertyData>().First();
            var orderNameTemplate = orderTemplate.Value.OfType<NamePropertyData>().First();
            var keyTemplate = (StructPropertyData)names.Value.GetItem(0).Key;
            var keyNameTemplate = keyTemplate.Value.OfType<NamePropertyData>().First();
            var textTemplate = (TextPropertyData)names.Value.GetItem(0).Value;

            // A tag the game already has must never be added twice — that would leave the menu data
            // with two entries for one shelf.
            var existing = order.Value.OfType<StructPropertyData>()
                .SelectMany(s => s.Value.OfType<NamePropertyData>())
                .Where(n => n.Name.ToString() == "TagName")
                .Select(n => n.Value.ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in names.Value)
            {
                if (pair.Key is StructPropertyData keyStruct
                    && keyStruct.Value.OfType<NamePropertyData>().FirstOrDefault() is { } name)
                    existing.Add(name.Value.ToString());
            }

            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section.Name) || string.IsNullOrWhiteSpace(section.Tag))
                    return new WriteResult(false, "A new menu section needs both a name and a tag.");

                if (!existing.Add(section.Tag))
                    return new WriteResult(false,
                        "The game's crafting menu already has a section with that name.");

                var tag = FName.FromString(asset, section.Tag);

                var entry = new StructPropertyData(orderTemplate.Name)
                {
                    StructType = orderTemplate.StructType,
                    SerializeNone = orderTemplate.SerializeNone,
                };
                entry.Value.Add(new NamePropertyData(orderNameTemplate.Name) { Value = tag });
                order.Value = order.Value.Append(entry).ToArray();

                var key = new StructPropertyData(keyTemplate.Name)
                {
                    StructType = keyTemplate.StructType,
                    SerializeNone = keyTemplate.SerializeNone,
                };
                key.Value.Add(new NamePropertyData(keyNameTemplate.Name) { Value = tag });

                var text = new TextPropertyData(textTemplate.Name)
                {
                    HistoryType = TextHistoryType.Base,
                    Namespace = FString.FromString(string.Empty),
                    CultureInvariantString = FString.FromString(section.Name),
                };
                text.Value = FString.FromString(NewTextKey());
                names.Value.Add(key, text);
            }

            asset.Write(uassetFilePath);
            return new WriteResult(true, null);
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Adding a crafting menu section failed ({ex.GetType().Name}).");
            return new WriteResult(false, "The game's crafting menu data could not be changed.");
        }
    }

    /// <summary>A fresh 32-character text key, shaped like the game's own.</summary>
    private static string NewTextKey() => Guid.NewGuid().ToString("N").ToUpperInvariant();
}
