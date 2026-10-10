namespace SCMW.Backend.Build;

/// <summary>Whether a package copy was changed; when not, one plain reason.</summary>
public sealed record WriteResult(bool Ok, string? Error);
