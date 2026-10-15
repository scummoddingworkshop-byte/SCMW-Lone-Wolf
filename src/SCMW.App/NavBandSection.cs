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

    /// <summary>The band's label, upper-cased for the rail heading (empty for the Home row, which
    /// sits above the labelled bands with no heading).</summary>
    public string Title => Band == NavBand.Home ? "" : Band.ToString().ToUpperInvariant();

    /// <summary>True when this band shows a heading (every band except Home).</summary>
    public bool HasTitle => Title.Length > 0;

    /// <summary>True for the single Home row above the bands (it gets extra breathing room).</summary>
    public bool IsHome => Band == NavBand.Home;

    /// <summary>The rows in this band, in rail order.</summary>
    public IReadOnlyList<NavItem> Items { get; }

    /// <summary>The row highlighted in this band (only one band has one at a time).</summary>
    [ObservableProperty] private NavItem? _selected;
}
