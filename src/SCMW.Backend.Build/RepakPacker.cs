namespace SCMW.Backend.Build;

/// <summary>Runs the bundled repak: `repak pack &lt;staging&gt; &lt;pak&gt;`. Never claims success without a file.</summary>
public sealed class RepakPacker : IPacker
{
    private readonly string _repakExePath;
    private readonly IProcessRunner _runner;

    public RepakPacker(string repakExePath, IProcessRunner runner)
    {
        _repakExePath = repakExePath;
        _runner = runner;
    }

    public async Task<PackResult> PackAsync(string stagingFolder, string pakPath, CancellationToken ct)
    {
        if (!File.Exists(_repakExePath))
            return new PackResult(false,
                "The packing tool is missing from the app, so the mod cannot be packed.");

        if (!Directory.Exists(stagingFolder) ||
            Directory.GetFiles(stagingFolder, "*", SearchOption.AllDirectories).Length == 0)
            return new PackResult(false, "There was nothing to pack.");

        var result = await _runner
            .RunAsync(_repakExePath, new[] { "pack", stagingFolder, pakPath }, ct)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            // The tool's own output can contain raw paths and jargon — keep it in the log, and give
            // the owner one plain sentence.
            SCMW.Core.Logging.Log.Warn($"repak failed (exit {result.ExitCode}): {result.Output}");
            return new PackResult(false,
                "The packing tool couldn't make the mod file. Close anything using SCUM's files and try again.");
        }

        if (!File.Exists(pakPath))
            return new PackResult(false, "The packing tool finished but no .pak file appeared.");

        return new PackResult(true, null);
    }
}
