namespace SCMW.Backend.Build;

/// <summary>Exactly what SCMW placed in SCUM, so removal is exact and survives a restart.</summary>
public sealed record InstallRecord(
    string ProjectId,
    string ProjectName,
    string Version,
    string PakFileName,
    string SourcePakPath,
    string InstalledPath,
    DateTime InstalledUtc,
    string? BackupPath);
