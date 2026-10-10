namespace SCMW.Backend.Build;

public interface IAssetResolver
{
    Task<AssetResolution> ResolveAsync(string recordedName, CancellationToken ct);
}
