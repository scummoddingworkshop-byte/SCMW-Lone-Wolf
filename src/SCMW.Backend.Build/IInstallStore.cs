namespace SCMW.Backend.Build;

/// <summary>Where "what we installed" lives. Never throws for a missing/corrupt record.</summary>
public interface IInstallStore
{
    InstallRecord? Load(string projectId);
    void Save(InstallRecord record);
    void Clear(string projectId);
}
