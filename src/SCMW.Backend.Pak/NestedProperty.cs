namespace SCMW.Backend.Pak;

/// <summary>
/// One property as the nested-value reader sees it, freed of the library's types so the walk
/// that turns it into dotted-path rows can be tested without a real game package.
/// </summary>
/// <param name="Name">The game's own property name for this step.</param>
/// <param name="TypeHint">The UE property type (e.g. "FloatProperty"), the honesty engine's clue.</param>
/// <param name="Value">The plain text of a leaf value, or null for a struct/reference we cannot show.</param>
/// <param name="Children">The values inside a struct, empty for a leaf.</param>
internal sealed record NestedProperty(
    string Name, string TypeHint, string? Value, IReadOnlyList<NestedProperty> Children)
{
    /// <summary>True when this step is a struct we can look inside.</summary>
    public bool IsStruct => Children.Count > 0;
}
