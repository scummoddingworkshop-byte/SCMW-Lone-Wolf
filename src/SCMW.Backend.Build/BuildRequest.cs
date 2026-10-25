namespace SCMW.Backend.Build;

/// <summary>Everything one build needs: the project's name/version, its changed items, any
/// brand-new assets it creates (clones plus their index entries), and any sections of its own to add
/// to the crafting menu.</summary>
public sealed record BuildRequest(string ProjectName, string Version, IReadOnlyList<BuildItem> Items,
    IReadOnlyList<NewBuildAsset>? NewAssets = null,
    IReadOnlyList<NewBuildSection>? Sections = null);
