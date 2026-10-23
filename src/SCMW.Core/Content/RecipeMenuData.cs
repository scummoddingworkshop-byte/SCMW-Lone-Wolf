// src/SCMW.Core/Content/RecipeMenuData.cs
// The shipped menu list (recipe-groups.json), written by the GameModel tool's --recipe-groups mode.
// It ships inside the assembly so the app needs no file paths; re-run the tool after a SCUM update.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCMW.Core.Content;

/// <summary>Every recipe's shelf and order as read from one game build.</summary>
public sealed record RecipeMenuFile(string GameBuild, string ReadOn, IReadOnlyList<RecipePlace> Recipes);

/// <summary>Loads the menu list embedded in this assembly. Never throws: a missing or broken file
/// reads as an empty list, so the app still opens and the card can say the list is unavailable.</summary>
public static class RecipeMenuData
{
    private const string ResourceName = "SCMW.Core.Content.recipe-groups.json";

    private static readonly Lazy<RecipeMenuFile> Shipped = new(Read,
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The list read from the game when this app was built.</summary>
    public static RecipeMenuFile Load() => Shipped.Value;

    private static RecipeMenuFile Read()
    {
        try
        {
            using var stream = typeof(RecipeMenuData).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return Empty;

            using var reader = new StreamReader(stream);
            var file = JsonSerializer.Deserialize<FileEntry>(reader.ReadToEnd(), Options);
            if (file?.Recipes is null) return Empty;

            var recipes = new List<RecipePlace>(file.Recipes.Count);
            foreach (var row in file.Recipes)
            {
                if (row is null || string.IsNullOrWhiteSpace(row.Tag) || string.IsNullOrWhiteSpace(row.Name))
                    continue;

                recipes.Add(new RecipePlace(row.Tag.Trim(), row.Name.Trim(), row.Priority));
            }

            return new RecipeMenuFile(file.GameBuild?.Trim() ?? string.Empty,
                file.ReadOn?.Trim() ?? string.Empty, recipes);
        }
        catch (Exception)
        {
            return Empty;
        }
    }

    private static readonly RecipeMenuFile Empty =
        new(string.Empty, string.Empty, Array.Empty<RecipePlace>());

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record FileEntry(string? GameBuild, string? ReadOn, List<Row>? Recipes);

    private sealed record Row(string? Tag, string? Name, int Priority);
}
