namespace SCMW.Backend.Build;

/// <summary>A recorded name turned into a game package path, or one plain reason it could not be.</summary>
public sealed record AssetResolution(string? PackagePath, string? Error);
