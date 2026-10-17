// src/SCMW.Core/Content/RecipeIngredients.cs
// The pure per-slot model of a recipe's ingredients. A recipe stores one amount per
// ingredient slot per crafting-skill tier, so a slot (tag + the five amounts) is the shape
// the screen edits. This file holds only data and grouping - no UI, no files.
using System.Globalization;
using SCMW.Core.Editing;

namespace SCMW.Core.Content;

/// <summary>
/// One recorded list change to a recipe's ingredient slots: add a slot (cloned from the last) or
/// remove the slot at <paramref name="Index"/>. Played back in recorded order to show the page.
/// </summary>
public sealed record IngredientListChange(ChangeOperation Operation, int Index);

/// <summary>One crafting-skill tier, in the order a recipe lists them (lowest first).</summary>
public enum SkillLevel
{
    NoSkill = 0,
    Basic = 1,
    Medium = 2,
    Advanced = 3,
    AboveAdvanced = 4,
}

/// <summary>One skill tier's amount for one ingredient slot.</summary>
public sealed record IngredientAmount(SkillLevel Level, AssetField Field);

/// <summary>
/// One ingredient slot of a recipe: the tag it accepts and the amount it needs at each skill tier
/// that the file carries. <paramref name="Index"/> is the slot's position in the file's own
/// <c>Ingredients</c> list, so every field path can be rebuilt exactly.
/// </summary>
public sealed record IngredientSlot(int Index, AssetField? Tag, IReadOnlyList<IngredientAmount> Amounts)
{
    /// <summary>The accepted tag's plain value (e.g. <c>CI_Metal_Scrap</c>), or null.</summary>
    public string? TagValue => Tag?.CurrentValue;

    /// <summary>The amount field for <paramref name="level"/>, or null when the file has none.</summary>
    public AssetField? AmountField(SkillLevel level)
    {
        foreach (var amount in Amounts)
        {
            if (amount.Level == level) return amount.Field;
        }

        return null;
    }
}

/// <summary>
/// Groups a recipe's nested fields into its ingredient slots. A slot's fields are addressed
/// <c>Ingredients[N].…</c> — its tag as <c>AllowedTypes[k]</c> and its amounts as
/// <c>Amount.&lt;Skill&gt;</c>; every other field is left for the plain field list.
/// <para>
/// This class is also the deliberate, bounded <b>exception to the field-library rule</b> ("the
/// library is the one source of what we can write"). A slot's values are <i>per-slot</i> paths
/// (<c>Ingredients[0]…</c>, <c>Ingredients[1]…</c>) that the fixed library cannot name, so the one
/// place those paths are built lives here — see <see cref="SlotPath"/>, <see cref="TagPath"/> and
/// <see cref="AmountPath"/>. Nothing else may invent an ingredient path, and this stays scoped to
/// recipes: every ordinary field is still written only through the library.
/// </para>
/// </summary>
public static class RecipeIngredients
{
    /// <summary>The list field a recipe's slots live in.</summary>
    public const string ListName = "Ingredients";

    /// <summary>The tag sub-field of a slot; its value is the accepted tag (usually one).</summary>
    private const string AllowedTypesPrefix = "AllowedTypes[";

    /// <summary>The amount sub-field of a slot; the rest names the skill tier.</summary>
    private const string AmountPrefix = "Amount.";

    /// <summary>The five skill tiers, paired with the field name the game uses for each.</summary>
    private static readonly (string Name, SkillLevel Level)[] Skills =
    {
        ("NoSkill", SkillLevel.NoSkill),
        ("Basic", SkillLevel.Basic),
        ("Medium", SkillLevel.Medium),
        ("Advanced", SkillLevel.Advanced),
        ("AboveAdvanced", SkillLevel.AboveAdvanced),
    };

    /// <summary>The recipe's ingredient slots, ordered by their list index. Never null.</summary>
    public static IReadOnlyList<IngredientSlot> From(IReadOnlyList<AssetField>? fields)
    {
        if (fields is null || fields.Count == 0)
            return Array.Empty<IngredientSlot>();

        var slots = new SortedDictionary<int, Builder>();
        foreach (var field in fields)
        {
            if (!TrySlot(field.TechnicalName, out var index, out var rest))
                continue;   // a plain field (Caption, ExperienceReward…) is not a slot

            if (!slots.TryGetValue(index, out var builder))
            {
                builder = new Builder(index);
                slots[index] = builder;
            }

            if (TryAllowedType(rest, out var order))
            {
                if (!builder.AllowedTypes.ContainsKey(order))
                    builder.AllowedTypes.Add(order, field);
            }
            else if (TrySkill(rest, out var skill))
            {
                if (!builder.Amounts.ContainsKey(skill))
                    builder.Amounts.Add(skill, field);
            }
        }

        var result = new List<IngredientSlot>(slots.Count);
        foreach (var builder in slots.Values)
            result.Add(builder.Build());
        return result;
    }

    /// <summary>
    /// The file's slots with the recipe's recorded list changes played back in order: an add appends
    /// a clone of the last slot, a remove drops the slot at its index. The result is renumbered from
    /// zero, matching how the build writes the list. Never null.
    /// </summary>
    public static IReadOnlyList<IngredientSlot> WithListChanges(
        IReadOnlyList<IngredientSlot>? slots, IReadOnlyList<IngredientListChange>? changes)
    {
        var working = slots is null ? new List<IngredientSlot>() : new List<IngredientSlot>(slots);

        if (changes is not null)
        {
            foreach (var change in changes)
            {
                switch (change.Operation)
                {
                    case ChangeOperation.AddEntry:
                        // The build clones the last entry; the card mirrors that.
                        working.Add(working.Count > 0 ? working[^1] : new IngredientSlot(0, null, Array.Empty<IngredientAmount>()));
                        break;

                    case ChangeOperation.RemoveEntry when change.Index >= 0 && change.Index < working.Count:
                        working.RemoveAt(change.Index);
                        break;
                }
            }
        }

        // The written list is renumbered from zero, so the card is too.
        var result = new List<IngredientSlot>(working.Count);
        for (var i = 0; i < working.Count; i++)
            result.Add(working[i] with { Index = i });
        return result;
    }

    /// <summary>Split <c>Ingredients[3].Rest</c> into its index (3) and the rest (<c>Rest</c>).</summary>
    private static bool TrySlot(string name, out int index, out string rest)
    {
        index = -1;
        rest = string.Empty;

        if (string.IsNullOrEmpty(name))
            return false;

        var prefix = ListName + "[";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var close = name.IndexOf(']', prefix.Length);
        if (close < 0)
            return false;

        if (!int.TryParse(name.AsSpan(prefix.Length, close - prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out index))
            return false;

        var afterClose = close + 1;
        rest = afterClose < name.Length && name[afterClose] == '.'
            ? name[(afterClose + 1)..]
            : string.Empty;
        return true;
    }

    /// <summary>Read the bracket order out of <c>AllowedTypes[k]</c>, or false for anything else.</summary>
    private static bool TryAllowedType(string rest, out int order)
    {
        order = -1;

        if (!rest.StartsWith(AllowedTypesPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var close = rest.IndexOf(']', AllowedTypesPrefix.Length);
        if (close < 0)
            return false;

        return int.TryParse(rest.AsSpan(AllowedTypesPrefix.Length, close - AllowedTypesPrefix.Length),
            NumberStyles.None, CultureInfo.InvariantCulture, out order);
    }

    /// <summary>Read the skill tier out of <c>Amount.&lt;Skill&gt;</c>, or false for anything else.</summary>
    private static bool TrySkill(string rest, out SkillLevel skill)
    {
        skill = default;

        if (!rest.StartsWith(AmountPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var name = rest[AmountPrefix.Length..];
        foreach (var (known, level) in Skills)
        {
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
            {
                skill = level;
                return true;
            }
        }

        return false;
    }

    /// <summary>The game's own field name for a skill tier (e.g. <c>AboveAdvanced</c>).</summary>
    public static string SkillName(SkillLevel level)
    {
        foreach (var (name, known) in Skills)
        {
            if (known == level) return name;
        }

        return level.ToString();
    }

    /// <summary>The path of one slot's list entry, e.g. <c>Ingredients[2]</c> (for add/remove).</summary>
    public static string SlotPath(int slotIndex) =>
        ListName + "[" + slotIndex.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The path of one slot's accepted tag, e.g. <c>Ingredients[2].AllowedTypes[0]</c>.</summary>
    public static string TagPath(int slotIndex) =>
        SlotPath(slotIndex) + "." + AllowedTypesPrefix + "0]";

    /// <summary>The path of one slot's amount at one skill tier, e.g. <c>Ingredients[2].Amount.Basic</c>.</summary>
    public static string AmountPath(int slotIndex, SkillLevel level) =>
        SlotPath(slotIndex) + "." + AmountPrefix + SkillName(level);

    /// <summary>The slot index a saved path names (<c>Ingredients[3]…</c>), or false for a non-slot path.</summary>
    public static bool TrySlotIndex(string? path, out int index)
    {
        index = -1;
        return path is not null && TrySlot(path, out index, out _);
    }

    /// <summary>Gathers one slot's fields while the recipe's field list is walked.</summary>
    private sealed class Builder
    {
        public Builder(int index) => Index = index;

        public int Index { get; }

        /// <summary>Accepted tags by their bracket order, so the lowest is the slot's tag.</summary>
        public SortedDictionary<int, AssetField> AllowedTypes { get; } = new();

        /// <summary>Amounts by skill tier, kept in tier order.</summary>
        public SortedDictionary<SkillLevel, AssetField> Amounts { get; } = new();

        public IngredientSlot Build()
        {
            var tag = AllowedTypes.Count > 0 ? AllowedTypes.First().Value : null;

            var amounts = new List<IngredientAmount>(Amounts.Count);
            foreach (var pair in Amounts)
                amounts.Add(new IngredientAmount(pair.Key, pair.Value));

            return new IngredientSlot(Index, tag, amounts);
        }
    }
}
