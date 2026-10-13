using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace SCMW.Backend.Build;

/// <summary>
/// Resolves a field path to the property it names. A plain name (<c>MaxStackSize</c>) is a
/// one-step path; a dotted path (<c>_replenishableResourceDescription.PeriodicReplenishment.
/// ReplenishAmount</c>) walks into structs to reach a value hidden inside them; a step may also
/// carry a list index (<c>Ingredients[0].Amount.Basic</c>) to reach a value inside a list entry.
/// Each name matches by the game's own name first, then by the friendly label, so an edit recorded
/// before technical names were kept still resolves.
/// </summary>
internal static class PropertyPath
{
    /// <summary>The steps of a dotted path; a plain name is one step. Empty for blank input.</summary>
    internal static IReadOnlyList<string> Steps(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? Array.Empty<string>()
            : path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Splits a step into a name and an optional list index: <c>Ingredients[0]</c> becomes
    /// <c>("Ingredients", 0)</c>; a bare <c>[2]</c> becomes <c>("", 2)</c>; a plain name gets -1.
    /// </summary>
    internal static (string Name, int Index) SplitStep(string step)
    {
        var open = step.IndexOf('[');
        if (open < 0 || !step.EndsWith(']')) return (step, -1);

        var name = step[..open];
        var inner = step[(open + 1)..^1];
        return int.TryParse(inner, out var index) && index >= 0 ? (name, index) : (step, -1);
    }

    /// <summary>
    /// The property <paramref name="path"/> points at, or null when a step is missing — or when a
    /// middle step is not a struct or list we can look inside.
    /// </summary>
    internal static PropertyData? Resolve(IReadOnlyList<PropertyData> properties, string? path)
    {
        var steps = Steps(path);
        if (steps.Count == 0) return null;

        IReadOnlyList<PropertyData> scope = properties;
        PropertyData? current = null;

        foreach (var step in steps)
        {
            var (name, index) = SplitStep(step);

            if (name.Length == 0)
            {
                // A bare index: step into the list we are already looking at.
                if (current is not ArrayPropertyData bare || index < 0 || index >= bare.Value.Length)
                    return null;
                current = bare.Value[index];
            }
            else
            {
                var names = scope.Select(p => p.Name.ToString()).ToList();
                var at = UAssetPackageWriter.FindPropertyIndex(names, name);
                if (at < 0) return null;
                current = scope[at];

                if (index >= 0)
                {
                    if (current is not ArrayPropertyData array || index >= array.Value.Length) return null;
                    current = array.Value[index];
                }
            }

            scope = current is StructPropertyData structure && structure.Value is { } children
                ? children
                : Array.Empty<PropertyData>();
        }

        return current;
    }

    /// <summary>
    /// The list a list-change names, plus the entry index for a removal (-1 for an add). Null when
    /// the field does not name a list. <c>Ingredients</c> names the whole list; <c>Ingredients[1]</c>
    /// names it and the entry to remove.
    /// </summary>
    internal static ArrayPropertyData? ResolveList(
        IReadOnlyList<PropertyData> properties, string? field, out int index)
    {
        index = -1;
        var steps = Steps(field);
        if (steps.Count == 0) return null;

        var (name, at) = SplitStep(steps[^1]);
        index = at;

        IReadOnlyList<PropertyData> scope = properties;
        if (steps.Count > 1)
        {
            var parent = Resolve(properties, string.Join('.', steps.Take(steps.Count - 1)));
            if (parent is not StructPropertyData structure || structure.Value is null) return null;
            scope = structure.Value;
        }

        var names = scope.Select(p => p.Name.ToString()).ToList();
        var found = UAssetPackageWriter.FindPropertyIndex(names, name);
        return found >= 0 && scope[found] is ArrayPropertyData array ? array : null;
    }
}
