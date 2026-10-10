namespace SCMW.Backend.Build;

/// <summary>Whether a `.pak` was produced; when not, one plain reason.</summary>
public sealed record PackResult(bool Ok, string? Error);
