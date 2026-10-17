namespace SCMW.Backend.Build;

/// <summary>Everything one build needs: the project's name/version, its changed items, and any
/// brand-new assets it creates (clones plus their index entries).</summary>
public sealed record BuildRequest(string ProjectName, string Version, IReadOnlyList<BuildItem> Items,
    IReadOnlyList<NewBuildAsset>? NewAssets = null);
