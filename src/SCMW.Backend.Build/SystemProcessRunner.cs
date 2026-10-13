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
        try
        {
            // Read both pipes at the same time: a tool that writes a lot to one pipe must never
            // block because we're waiting on the other.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, (stdout + stderr).Trim());
        }
        catch (OperationCanceledException)
        {
            // Never leave the tool running after the caller gave up.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }
    }
}
