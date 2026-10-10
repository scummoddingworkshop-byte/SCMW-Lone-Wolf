using System.Diagnostics;

namespace SCMW.Backend.Build;

/// <summary>The real runner: no shell, no window, output captured, cancelled with the caller.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, (stdout + stderr).Trim());
    }
}
