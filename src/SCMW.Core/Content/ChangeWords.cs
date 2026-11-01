using System.Globalization;

namespace SCMW.Core.Content;

/// <summary>
/// The plain words for one value's technical path, as the Build list shows it. A path into a dish
/// (<c>OptionalIngredients[1].PossibleIngredient[0].Item</c>) or a recipe (<c>Ingredients[1].Tag</c>)
/// is written with the same numbering the cards use — so "optional ingredient 2" is the second one the
/// owner sees — and every other field keeps the field library's own name.
/// </summary>
public static class ChangeWords
{
    /// <summary>The owner-facing words for a changed value's technical name. Never throws.</summary>
    public static string For(string? technicalName)
    {
        if (string.IsNullOrWhiteSpace(technicalName)) return string.Empty;

        var path = technicalName.Trim();

        var dish = DishWords(path);
        if (dish is not null) return dish;

        var recipe = RecipeWords(path);
        if (recipe is not null) return recipe;

        return FieldNames.Friendly(path);
    }

    /// <summary>
    /// The order a changed value's row sits in on a card. A recipe's ingredients travel together: every
    /// "what it accepts" first, one ingredient after another, then the amounts from no skill upwards —
    /// so an ingredient's own numbers sit below the ingredients instead of splitting them. Everything
    /// else keeps the plain alphabetical order. Rows with the same key keep the order they were read in.
    /// </summary>
    public static string OrderKey(string? technicalName)
    {
        var path = (technicalName ?? string.Empty).Trim();
        if (path.Length == 0) return string.Empty;

        return RecipeOrderKey(path) ?? path;
    }

    /// <summary>The order key for a recipe's ingredient path, or null when the path is not one.</summary>
    private static string? RecipeOrderKey(string path)
    {
        if (!StartsWith(path, RecipeIngredients.ListName, out var after)) return null;
        if (!TakeIndex(after, out var index, out var rest)) return null;

        var head = RecipeIngredients.ListName + "#";
        var slot = index.ToString("000", CultureInfo.InvariantCulture);

        if (rest.Length == 0) return head + "0" + slot;                        // the whole ingredient
        if (StartsWith(rest, "AllowedTypes", out _)) return head + "1" + slot;  // what it accepts

        if (StartsWith(rest, "Amount", out var afterAmount) && afterAmount.StartsWith(".", StringComparison.Ordinal))
            return head + "2" + slot + SkillOrder(afterAmount[1..]);

        return head + "9" + slot;                                               // anything else on it
    }

    /// <summary>The five skill levels in the order the cards show them, so the amounts count upwards.</summary>
    private static string SkillOrder(string skill) => skill.ToUpperInvariant() switch
    {
        "NOSKILL" => "0",
        "BASIC" => "1",
        "MEDIUM" => "2",
        "ADVANCED" => "3",
        "ABOVEADVANCED" => "4",
        _ => "5" + skill,
    };

    /// <summary>A dish's ingredient path, or null when the path is not one.</summary>
    private static string? DishWords(string path)
    {
        string list; string rest;
        if (StartsWith(path, CookingIngredients.MainListName, out var afterMain))
        {
            list = "Main ingredient";
            rest = afterMain;
        }
        else if (StartsWith(path, CookingIngredients.OptionalListName, out var afterOptional))
        {
            list = "Optional ingredient";
            rest = afterOptional;
        }
        else return null;

        // "……[index]" is the whole ingredient (an add or a remove).
        if (!TakeIndex(rest, out var index, out var after)) return null;
        var words = list + " " + Number(index);

        if (after.Length == 0) return words;

        if (Is(after, CookingIngredients.TitleName)) return words + " · title";
        if (Is(after, CookingIngredients.ResourceFlagName)) return words + " · is a resource";

        if (!StartsWith(after, CookingIngredients.FoodListName, out var afterFood)) return words + " · " + Lower(after);

        words += " · foods";
        if (afterFood.Length == 0) return words;
        if (!TakeIndex(afterFood, out var food, out var foodRest)) return words;

        words = words[..^" · foods".Length] + " · food " + Number(food);
        if (foodRest.Length == 0) return words;

        if (Is(foodRest, CookingIngredients.ItemName)) return words + " · item";
        if (Is(foodRest, CookingIngredients.ResourceTypeName)) return words + " · resource";
        if (Is(foodRest, CookingIngredients.ResourceFlagName)) return words + " · is a resource";
        return words + " · " + Lower(foodRest);
    }

    /// <summary>A recipe's ingredient path, or null when the path is not one.</summary>
    private static string? RecipeWords(string path)
    {
        if (!StartsWith(path, RecipeIngredients.ListName, out var after)) return null;
        if (!TakeIndex(after, out var index, out var rest)) return null;

        var words = "Ingredient " + Number(index);
        if (rest.Length == 0) return words;

        if (StartsWith(rest, "AllowedTypes", out _)) return words + " · what it accepts";

        if (StartsWith(rest, "Amount", out var afterAmount) && afterAmount.StartsWith(".", StringComparison.Ordinal))
        {
            var skill = Spaced(afterAmount[1..]).ToLowerInvariant();
            return words + " · amount (" + skill + ")";
        }

        return words + " · " + Lower(rest);
    }

    private static bool StartsWith(string text, string prefix, out string rest)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = text[prefix.Length..];
            return true;
        }

        rest = string.Empty;
        return false;
    }

    /// <summary>Reads "[n]" at the start of <paramref name="text"/>; the rest is what follows it.</summary>
    private static bool TakeIndex(string text, out int index, out string rest)
    {
        index = 0;
        rest = string.Empty;
        if (text.Length < 3 || text[0] != '[') return false;

        var close = text.IndexOf(']');
        if (close < 2) return false;
        if (!int.TryParse(text.AsSpan(1, close - 1), NumberStyles.None, CultureInfo.InvariantCulture, out index))
            return false;

        rest = text[(close + 1)..];
        if (rest.StartsWith('.')) rest = rest[1..];
        return true;
    }

    /// <summary>True when <paramref name="text"/> is exactly this field name (with or without a leading dot).</summary>
    private static bool Is(string text, string name) =>
        string.Equals(text, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "." + name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The owner-facing name for a value field, starting in lower case ("Nutrient Incl.…" → "nutrient…").</summary>
    private static string Lower(string field)
    {
        var text = FieldNames.Friendly(field.TrimStart('.')).Trim();
        return text.Length == 0 ? string.Empty : char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static string Number(int index) => (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>"AboveAdvanced" → "Above Advanced": a space before a capital that follows a small letter.</summary>
    private static string Spaced(string text)
    {
        if (text.Length == 0) return text;

        var words = new System.Text.StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1])) words.Append(' ');
            words.Append(text[i]);
        }

        return words.ToString();
    }
}
