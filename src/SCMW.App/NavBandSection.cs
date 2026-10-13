using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>
/// One labelled band of the rail (Make · Ship · App) and the rows under it. Each band keeps its
/// own highlighted row, so the shell can show exactly one selection across the whole rail.
/// </summary>
public partial class NavBandSection : ObservableObject
{
    public NavBandSection(NavBand band, IReadOnlyList<NavItem> items)
    {
        Band = band;
        Items = items;
    }

    /// <summary>Which band this is.</summary>
    public NavBand Band { get; }

    /// <summary>The band's label, upper-cased for the rail heading.</summary>
    public string Title => Band.ToString().ToUpperInvariant();

    /// <summary>The rows in this band, in rail order.</summary>
    public IReadOnlyList<NavItem> Items { get; }

    /// <summary>The row highlighted in this band (only one band has one at a time).</summary>
    [ObservableProperty] private NavItem? _selected;
}
