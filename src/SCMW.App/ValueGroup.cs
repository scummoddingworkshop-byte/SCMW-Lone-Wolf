using System.Collections.Generic;

namespace SCMW.App;

/// <summary>One section of values on the Items screen (Inventory, Damage, Weight…).</summary>
/// <param name="RemovePath">For an ingredient slot, the list field to remove ("Ingredients[0]"); else null.</param>
public sealed record ValueGroup(string Name, IReadOnlyList<ItemValue> Values, string? RemovePath = null);
