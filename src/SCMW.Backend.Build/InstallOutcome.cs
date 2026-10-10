namespace SCMW.Backend.Build;

public sealed record InstallOutcome(bool Ok, string? InstalledPath, string? Error,
    WriteFailureKind Failure = WriteFailureKind.None);
