// src/SCMW.Core/Content/ContentLimits.cs
// Shared search-result caps so the UI, the pak source and the SQLite index can
// never drift from each other. A single page of results is capped at MaxTake;
// the Assets browser opens with InitialPageSize and grows its window toward
// MaxTake via "Load more".
namespace SCMW.Core.Content;

public static class ContentLimits
{
    /// <summary>Hard ceiling on how many rows one search may return.</summary>
    public const int MaxTake = 500;

    /// <summary>Rows the Assets browser shows before the user asks for more.</summary>
    public const int InitialPageSize = 100;
}
