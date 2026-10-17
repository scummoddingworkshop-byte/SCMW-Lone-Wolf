// src/SCMW.Core/Content/CookingIngredients.cs
// The pure per-slot model of a cooking dish's ingredients. A dish stores main and optional
// ingredients, each with a title and a list of foods it may take (a food points at an item by a
// soft link and carries a few values). This file holds only data and grouping - no UI, no files.
using System.Globalization;
using SCMW.Core.Editing;

namespace SCMW.Core.Content;

/// <summary>
/// One recorded list change to a dish: add a food or an ingredient (cloned from the last), or remove
/// the one at <paramref name="FoodIndex"/> / <paramref name="IngredientIndex"/>. Played back in
/// recorded order to show the page.
/// </summary>
public sealed record CookingListChange(ChangeOperation Operation, bool IsMain, int IngredientIndex,
                                       bool IsFood, int FoodIndex);

/// <summary>A saved path's shape, read back so a recorded change can be replayed.</summary>
public sealed record CookingListPath(bool IsMain, int IngredientIndex, bool IsFood, int FoodIndex);

/// <summary>One of a food's own values, paired with the game's field name (e.g. <c>Liters</c>).</summary>
public sealed record CookingFoodValue(string Name, AssetField Field);

/// <summary>
/// One food a dish's ingredient may take: the item it points at (a soft link) and the values it
/// carries. <paramref name="Index"/> is its position in the ingredient's own <c>PossibleIngredient</c>
/// list, so every field path can be rebuilt exactly.
/// </summary>
public sealed record CookingFoodSlot(int Index, AssetField? Item, AssetField? ResourceType,
                                     IReadOnlyList<CookingFoodValue> Values)
{
    /// <summary>The item's soft object path as the reader shows it, or null.</summary>
    public string? ItemValue => Item?.CurrentValue;

    /// <summary>True when the food points at a real item (a normal food, not a resource).</summary>
    public bool HasItem =>
        Item is not null && ItemValue is { Length: > 0 } value
        && !string.Equals(value, "None", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The resource's plain name (the game keeps milk/water in <c>ResourceType</c>, e.g.
    /// <c>Milk_C</c> → <c>Milk</c>), or null when there is no resource.
    /// </summary>
    public string? ResourceName => PlainName(ResourceType?.CurrentValue);

    /// <summary>The value field named <paramref name="name"/> (e.g. <c>Liters</c>), or null.</summary>
    public AssetField? ValueField(string name)
    {
        foreach (var value in Values)
        {
            if (string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)) return value.Field;
        }

        return null;
    }

    /// <summary>A resource link's plain words: drop the trailing <c>_C</c>, underscores and
    /// camel-case joins become spaces (<c>DrinkableWater_C</c> → <c>Drinkable Water</c>).</summary>
    private static string? PlainName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim();
        if (text.EndsWith("_C", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        text = text.Replace('_', ' ');

        // "DrinkableWater" reads as "Drinkable Water": a space before a capital that follows a small letter.
        var words = new System.Text.StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1])) words.Append(' ');
            words.Append(text[i]);
        }

        text = words.ToString().Trim();
        while (text.Contains("  ", StringComparison.Ordinal)) text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text.Length == 0 ? null : text;
    }
}

/// <summary>
/// One ingredient of a dish: whether it is main or optional, its title, and the foods it may take.
/// <paramref name="Index"/> is its position in its own list (<c>MainIngredients</c> or
/// <c>OptionalIngredients</c>), so every field path can be rebuilt exactly.
/// </summary>
public sealed record CookingIngredientSlot(bool IsMain, int Index, AssetField? Title,
                                           IReadOnlyList<CookingFoodSlot> Foods)
{
    /// <summary>The title's plain value (e.g. <c>Base</c>), or null.</summary>
    public string? TitleValue => Title?.CurrentValue;

    /// <summary>The list this ingredient lives in.</summary>
    public string ListName => CookingIngredients.ListName(IsMain);
}

/// <summary>
/// Groups a dish's nested fields into its ingredients and their foods. An ingredient's fields are
/// addressed <c>MainIngredients[N].…</c> / <c>OptionalIngredients[N].…</c>, its title as
/// <c>IngredientTitle</c>, and a food's values as <c>PossibleIngredient[M].Item</c> and friends;
/// every other field (e.g. a food's <c>ResourceType</c> link) is left for the plain field list.
/// <para>
/// This class is also the deliberate, bounded <b>exception to the field-library rule</b> ("the
/// library is the one source of what we can write"). A dish's values are <i>per-slot</i> paths the
/// fixed library cannot name, so the one place those paths are built lives here - see
/// <see cref="IngredientPath"/>, <see cref="TitlePath"/>, <see cref="FoodItemPath"/> and
/// <see cref="FoodValuePath"/>. Nothing else may invent a cooking path, and this stays scoped to
/// dishes: every ordinary field is still written only through the library.
/// </para>
/// </summary>
public static class CookingIngredients
{
    /// <summary>The two lists a dish's ingredients live in.</summary>
    public const string MainListName = "MainIngredients";
    public const string OptionalListName = "OptionalIngredients";

    /// <summary>The title sub-field of an ingredient.</summary>
    public const string TitleName = "IngredientTitle";

    /// <summary>The foods sub-list of an ingredient.</summary>
    public const string FoodListName = "PossibleIngredient";

    /// <summary>The soft-link sub-field of a food.</summary>
    public const string ItemName = "Item";

    /// <summary>The resource-link sub-field of a food (where the game keeps milk/water).</summary>
    public const string ResourceTypeName = "ResourceType";

    /// <summary>The food value sub-fields we own and show, in the order the card shows them.</summary>
    public static readonly string[] FoodValueNames =
    {
        "Liters", "Usage", "NutrientInclusionFactor", "ShouldConsumeWhole", "IsResource",
    };

    /// <summary>The dish's ingredients, main first then optional, ordered by list index. Never null.</summary>
    public static IReadOnlyList<CookingIngredientSlot> From(IReadOnlyList<AssetField>? fields)
    {
        if (fields is null || fields.Count == 0)
            return Array.Empty<CookingIngredientSlot>();

        var main = new SortedDictionary<int, IngredientBuilder>();
        var optional = new SortedDictionary<int, IngredientBuilder>();

        foreach (var field in fields)
        {
            if (!TryIngredientPath(field.TechnicalName, out var isMain, out var index, out var rest))
                continue;

            var list = isMain ? main : optional;
            if (!list.TryGetValue(index, out var builder))
            {
                builder = new IngredientBuilder(isMain, index);
                list[index] = builder;
            }

            builder.Add(rest, field);
        }

        var result = new List<CookingIngredientSlot>(main.Count + optional.Count);
        foreach (var builder in main.Values) result.Add(builder.Build());
        foreach (var builder in optional.Values) result.Add(builder.Build());
        return result;
    }

    /// <summary>
    /// The dish's ingredients with the recorded list changes played back in order: an add appends a
    /// clone of the last entry of the same list (an ingredient's last food), a remove drops the entry
    /// at its index. The result is renumbered from zero, matching how the build writes the lists.
    /// Never null.
    /// </summary>
    public static IReadOnlyList<CookingIngredientSlot> WithListChanges(
        IReadOnlyList<CookingIngredientSlot>? ingredients, IReadOnlyList<CookingListChange>? changes)
    {
        var main = new List<CookingIngredientSlot>();
        var optional = new List<CookingIngredientSlot>();
        if (ingredients is not null)
        {
            foreach (var ingredient in ingredients)
                (ingredient.IsMain ? main : optional).Add(ingredient);
        }

        if (changes is not null)
        {
            foreach (var change in changes)
            {
                var list = change.IsMain ? main : optional;

                if (!change.IsFood)
                {
                    switch (change.Operation)
                    {
                        case ChangeOperation.AddEntry:
                            // The build clones the last entry; the card mirrors that.
                            list.Add(list.Count > 0
                                ? list[^1]
                                : new CookingIngredientSlot(change.IsMain, 0, null, Array.Empty<CookingFoodSlot>()));
                            break;

                        case ChangeOperation.RemoveEntry
                            when change.IngredientIndex >= 0 && change.IngredientIndex < list.Count:
                            list.RemoveAt(change.IngredientIndex);
                            break;
                    }

                    continue;
                }

                var at = change.IngredientIndex;
                if (at < 0 || at >= list.Count) continue;   // a stale change against a gone ingredient

                var ingredient = list[at];
                var foods = new List<CookingFoodSlot>(ingredient.Foods);
                switch (change.Operation)
                {
                    case ChangeOperation.AddEntry:
                        foods.Add(foods.Count > 0
                            ? foods[^1]
                            : new CookingFoodSlot(0, null, null, Array.Empty<CookingFoodValue>()));
                        break;

                    case ChangeOperation.RemoveEntry when change.FoodIndex >= 0 && change.FoodIndex < foods.Count:
                        foods.RemoveAt(change.FoodIndex);
                        break;
                }

                list[at] = ingredient with { Foods = foods };
            }
        }

        // The written lists are renumbered from zero, so the card is too.
        var result = new List<CookingIngredientSlot>(main.Count + optional.Count);
        for (var i = 0; i < main.Count; i++) result.Add(Renumber(main[i], i));
        for (var i = 0; i < optional.Count; i++) result.Add(Renumber(optional[i], i));
        return result;
    }

    /// <summary>The game's own field name for an ingredient's list (main or optional).</summary>
    public static string ListName(bool isMain) => isMain ? MainListName : OptionalListName;

    /// <summary>The path of one ingredient's list entry, e.g. <c>MainIngredients[2]</c>.</summary>
    public static string IngredientPath(bool isMain, int index) =>
        ListName(isMain) + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The path of one ingredient's title, e.g. <c>MainIngredients[2].IngredientTitle</c>.</summary>
    public static string TitlePath(bool isMain, int index) => IngredientPath(isMain, index) + "." + TitleName;

    /// <summary>The path of one ingredient's food list, e.g. <c>MainIngredients[2].PossibleIngredient</c>.</summary>
    public static string FoodListPath(bool isMain, int index) =>
        IngredientPath(isMain, index) + "." + FoodListName;

    /// <summary>The path of one food, e.g. <c>MainIngredients[2].PossibleIngredient[3]</c>.</summary>
    public static string FoodPath(bool isMain, int index, int foodIndex) =>
        FoodListPath(isMain, index) + "[" + foodIndex.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The path of one food's item, e.g. <c>MainIngredients[0].PossibleIngredient[1].Item</c>.</summary>
    public static string FoodItemPath(bool isMain, int index, int foodIndex) =>
        FoodPath(isMain, index, foodIndex) + "." + ItemName;

    /// <summary>The path of one food's resource link, e.g. <c>…PossibleIngredient[1].ResourceType</c>.</summary>
    public static string FoodResourcePath(bool isMain, int index, int foodIndex) =>
        FoodPath(isMain, index, foodIndex) + "." + ResourceTypeName;

    /// <summary>The path of one food's value, e.g. <c>MainIngredients[0].PossibleIngredient[1].Liters</c>.</summary>
    public static string FoodValuePath(bool isMain, int index, int foodIndex, string valueName) =>
        FoodPath(isMain, index, foodIndex) + "." + valueName;

    /// <summary>True when <paramref name="name"/> is one of the food values the card owns.</summary>
    public static bool IsFoodValue(string name) =>
        Array.Exists(FoodValueNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The shape a saved list path names, or false for anything that is not a list change.</summary>
    public static bool TryListPath(string? path, out CookingListPath result)
    {
        result = null!;
        if (string.IsNullOrEmpty(path)) return false;

        bool isMain;
        string rest;
        if (StartsWithStep(path, MainListName, out var afterMain)) { isMain = true; rest = afterMain; }
        else if (StartsWithStep(path, OptionalListName, out var afterOptional)) { isMain = false; rest = afterOptional; }
        else return false;

        // The whole list: an add.
        if (rest.Length == 0)
        {
            result = new CookingListPath(isMain, -1, IsFood: false, FoodIndex: -1);
            return true;
        }

        // An ingredient entry: "[i]", optionally followed by ".PossibleIngredient[j]".
        if (rest[0] != '[') return false;
        var close = rest.IndexOf(']');
        if (close < 0) return false;
        if (!int.TryParse(rest.AsSpan(1, close - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var ingredientIndex))
            return false;

        var after = rest[(close + 1)..];
        if (after.Length == 0)
        {
            result = new CookingListPath(isMain, ingredientIndex, IsFood: false, FoodIndex: -1);
            return true;
        }

        if (after[0] != '.' || !StartsWithStep(after[1..], FoodListName, out var afterFood))
            return false;

        var foodIndex = -1;
        if (afterFood.Length > 0)
        {
            if (afterFood[0] != '[') return false;
            var foodClose = afterFood.IndexOf(']');
            if (foodClose < 0 || afterFood.Length != foodClose + 1) return false;
            if (!int.TryParse(afterFood.AsSpan(1, foodClose - 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out foodIndex))
                return false;
        }

        result = new CookingListPath(isMain, ingredientIndex, IsFood: true, FoodIndex: foodIndex);
        return true;
    }

    /// <summary>Split <c>MainIngredients[3].Rest</c> into main/optional, the index (3) and the rest.</summary>
    private static bool TryIngredientPath(string name, out bool isMain, out int index, out string rest)
    {
        isMain = false;
        index = -1;
        rest = string.Empty;

        if (string.IsNullOrEmpty(name)) return false;

        string after;
        if (StartsWithStep(name, MainListName, out var afterMain)) { isMain = true; after = afterMain; }
        else if (StartsWithStep(name, OptionalListName, out var afterOptional)) { isMain = false; after = afterOptional; }
        else return false;

        if (after.Length == 0 || after[0] != '[') return false;
        var close = after.IndexOf(']');
        if (close < 0) return false;
        if (!int.TryParse(after.AsSpan(1, close - 1), NumberStyles.None, CultureInfo.InvariantCulture, out index))
            return false;

        var tail = after[(close + 1)..];
        rest = tail.Length > 0 && tail[0] == '.' ? tail[1..] : string.Empty;
        return true;
    }

    /// <summary>Split <c>PossibleIngredient[3].Rest</c> into the food index (3) and the rest.</summary>
    private static bool TryFoodPath(string rest, out int foodIndex, out string foodRest)
    {
        foodIndex = -1;
        foodRest = string.Empty;

        if (!StartsWithStep(rest, FoodListName, out var after) || after.Length == 0 || after[0] != '[')
            return false;

        var close = after.IndexOf(']');
        if (close < 0) return false;
        if (!int.TryParse(after.AsSpan(1, close - 1), NumberStyles.None, CultureInfo.InvariantCulture, out foodIndex))
            return false;

        var tail = after[(close + 1)..];
        foodRest = tail.Length > 0 && tail[0] == '.' ? tail[1..] : string.Empty;
        return true;
    }

    /// <summary>True when <paramref name="path"/> starts with the step <paramref name="name"/>.</summary>
    private static bool StartsWithStep(string path, string name, out string remainder)
    {
        if (path.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            remainder = path[name.Length..];
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    /// <summary>Give an ingredient (and its foods) their zero-based positions after a change.</summary>
    private static CookingIngredientSlot Renumber(CookingIngredientSlot ingredient, int index)
    {
        var foods = new List<CookingFoodSlot>(ingredient.Foods.Count);
        for (var i = 0; i < ingredient.Foods.Count; i++)
            foods.Add(ingredient.Foods[i] with { Index = i });
        return ingredient with { Index = index, Foods = foods };
    }

    /// <summary>Gathers one ingredient's fields while the dish's field list is walked.</summary>
    private sealed class IngredientBuilder
    {
        public IngredientBuilder(bool isMain, int index)
        {
            IsMain = isMain;
            Index = index;
        }

        public bool IsMain { get; }

        public int Index { get; }

        public AssetField? Title { get; private set; }

        /// <summary>Foods by their bracket order, so the lowest is first.</summary>
        public SortedDictionary<int, FoodBuilder> Foods { get; } = new();

        public void Add(string rest, AssetField field)
        {
            if (string.Equals(rest, TitleName, StringComparison.OrdinalIgnoreCase))
            {
                Title ??= field;
                return;
            }

            if (!TryFoodPath(rest, out var foodIndex, out var foodRest)) return;

            if (!Foods.TryGetValue(foodIndex, out var food))
            {
                food = new FoodBuilder();
                Foods[foodIndex] = food;
            }

            if (string.Equals(foodRest, ItemName, StringComparison.OrdinalIgnoreCase))
                food.Item ??= field;
            else if (string.Equals(foodRest, ResourceTypeName, StringComparison.OrdinalIgnoreCase))
                food.ResourceType ??= field;
            else if (IsFoodValue(foodRest))
                food.AddValue(foodRest, field);
            // Any other food field is left for the plain field list.
        }

        public CookingIngredientSlot Build()
        {
            var foods = new List<CookingFoodSlot>(Foods.Count);
            foreach (var pair in Foods) foods.Add(pair.Value.Build(pair.Key));
            return new CookingIngredientSlot(IsMain, Index, Title, foods);
        }
    }

    /// <summary>Gathers one food's fields while the ingredient's field list is walked.</summary>
    private sealed class FoodBuilder
    {
        public AssetField? Item { get; set; }

        public AssetField? ResourceType { get; set; }

        private readonly Dictionary<string, AssetField> _values = new(StringComparer.OrdinalIgnoreCase);

        public void AddValue(string name, AssetField field)
        {
            if (!_values.ContainsKey(name)) _values[name] = field;
        }

        public CookingFoodSlot Build(int index)
        {
            // The card shows the values in a fixed order, whatever order the file lists them.
            var values = new List<CookingFoodValue>(_values.Count);
            foreach (var name in FoodValueNames)
            {
                if (_values.TryGetValue(name, out var field))
                    values.Add(new CookingFoodValue(name, field));
            }

            return new CookingFoodSlot(index, Item, ResourceType, values);
        }
    }
}
