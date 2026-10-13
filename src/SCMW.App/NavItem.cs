using Avalonia.Media;

namespace SCMW.App;

/// <summary>
/// One row of the shell rail: an area, its label, and its icons — a light one for the normal row and a
/// dark one for the selected row (which sits on the copper accent).
/// </summary>
public sealed record NavItem(ScreenKey Key, string Label,
                             IImage? Icon = null, IImage? IconSelected = null)
{
    /// <summary>Which band of the rail this row sits in.</summary>
    public NavBand Band => NavBands.For(Key);
}
