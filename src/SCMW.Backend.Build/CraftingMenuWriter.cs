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
    /// line exactly as it was. A tag already in the file is left alone (never duplicated).
    /// </summary>
    public static string AddTags(string? iniText, IEnumerable<string> tags)
    {
        var text = iniText ?? string.Empty;
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var line = TagLine(tag);
            if (text.Split('\n').Any(existing => existing.Trim() == line)) continue;

            if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal))
                text += "\n";
            text += line + "\n";
        }
        return text;
    }

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

            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section.Name) || string.IsNullOrWhiteSpace(section.Tag))
                    return new WriteResult(false, "A new menu section needs both a name and a tag.");

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
