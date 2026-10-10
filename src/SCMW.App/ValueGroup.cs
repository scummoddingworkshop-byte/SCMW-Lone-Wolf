using System.Collections.Generic;

namespace SCMW.App;

/// <summary>One section of values on the Items screen (Inventory, Damage, Weight…).</summary>
public sealed record ValueGroup(string Name, IReadOnlyList<ItemValue> Values);
