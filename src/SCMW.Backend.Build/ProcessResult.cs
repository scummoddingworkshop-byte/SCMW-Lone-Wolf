namespace SCMW.Backend.Build;

/// <summary>The outcome of one external tool run: exit code + combined output.</summary>
public sealed record ProcessResult(int ExitCode, string Output);
