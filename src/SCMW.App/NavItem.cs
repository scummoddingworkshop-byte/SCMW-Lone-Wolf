using Avalonia.Media;

namespace SCMW.App;

/// <summary>
/// One row of the shell rail: an area (or a job), its label, and its icons — a light one for the normal
/// row and a dark one for the selected row (which sits on the copper accent).
/// </summary>
public sealed record NavItem(ScreenKey Key, string Label,
                             IImage? Icon = null, IImage? IconSelected = null)
{
    /// <summary>Which band of the rail this row sits in.</summary>
    public NavBand Band => NavBands.For(this);

    /// <summary>True for an area that exists but is kept out of the rail for now.</summary>
    public bool Hidden { get; init; }

    /// <summary>
    /// For a row that opens a list rather than an area: the Workspace list it picks (Items, Recipes…).
    /// Null for every other row.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>True for a MAKE row: it opens the list and starts a brand-new asset.</summary>
    public bool Creates { get; init; }

    /// <summary>True for a described idea that is not built yet: shown, but greyed and not pickable.</summary>
    public bool ComingSoon { get; init; }

    /// <summary>True when the row can be picked (everything except a "coming soon" idea).</summary>
    public bool CanPick => !ComingSoon;
}
