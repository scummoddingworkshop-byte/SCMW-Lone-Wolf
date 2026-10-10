namespace SCMW.Backend.Build;

public sealed record RemoveOutcome(bool Ok, string? Error,
    WriteFailureKind Failure = WriteFailureKind.None);
