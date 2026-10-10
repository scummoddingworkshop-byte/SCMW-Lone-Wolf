namespace SCMW.Backend.Build;

/// <summary>Everything one build needs: the project's name/version and its changed items.</summary>
public sealed record BuildRequest(string ProjectName, string Version, IReadOnlyList<BuildItem> Items);
