// src/SCMW.Core/Content/ScanInfo.cs
// Snapshot of the last successful scan recorded in the asset index, surfaced
// to the Settings screen ("Last scan: {time} — {n} items from build {id}").
namespace SCMW.Core.Content;

public sealed record ScanInfo(DateTime? ScannedAtUtc, int ItemCount, string? BuildId);
