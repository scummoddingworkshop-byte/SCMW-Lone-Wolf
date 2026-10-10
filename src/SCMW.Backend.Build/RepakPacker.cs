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
            return new PackResult(false, "The packing tool reported a problem: " + result.Output);

        if (!File.Exists(pakPath))
            return new PackResult(false, "The packing tool finished but no .pak file appeared.");

        return new PackResult(true, null);
    }
}
