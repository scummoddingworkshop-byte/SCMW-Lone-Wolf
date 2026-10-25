namespace SCMW.Backend.Build;

/// <summary>
/// One section of the mod's own to add to the game's crafting menu: the words shown ("SCMW Mods")
/// and the game tag behind them (<c>CraftingCategory.Items.SCMW_Mods</c>). The tag must also be
/// registered in the game's tag list, which the build does alongside this.
/// </summary>
public sealed record NewBuildSection(string Name, string Tag);
