// src/SCMW.Core/Content/AssetSummary.cs
// One row of the asset browser: enough to list and filter assets without
// reading their full field data.
namespace SCMW.Core.Content;

public sealed record AssetSummary(string Id, string Name, string Category,
    StatusKind Overall, int EditableCount, string TechnicalPath, string? IconKey);
