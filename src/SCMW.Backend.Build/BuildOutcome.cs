namespace SCMW.Backend.Build;

/// <summary>The built `.pak`'s path, or one plain reason the build failed.</summary>
public sealed record BuildOutcome(bool Ok, string? PakPath, string? Error);
