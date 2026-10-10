namespace SCMW.Backend.Build;

/// <summary>Packs a folder tree into a `.pak`. Fails plainly; never throws for tool problems.</summary>
public interface IPacker
{
    Task<PackResult> PackAsync(string stagingFolder, string pakPath, CancellationToken ct);
}
