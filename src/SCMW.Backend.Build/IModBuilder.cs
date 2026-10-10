namespace SCMW.Backend.Build;

public interface IModBuilder
{
    Task<BuildOutcome> BuildAsync(BuildRequest request, IProgress<int>? progress, CancellationToken ct);
}
