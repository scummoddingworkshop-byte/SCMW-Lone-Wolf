namespace SCMW.Backend.Build;

/// <summary>Runs one external program. Abstracted so tests never start a real process.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, CancellationToken ct);
}
