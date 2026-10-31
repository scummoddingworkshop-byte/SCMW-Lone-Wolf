using System;
using System.Collections.Generic;
using System.Linq;

namespace SCMW.App;

/// <summary>The rail's bands, top to bottom (design §5). <see cref="Top"/> is the unlabelled group at
/// the top, which holds the places a player goes rather than the work they do.</summary>
public enum NavBand
{
    Top,
    Modify,
    Make,
    Ship,
    App,
}

/// <summary>One band and the rail rows that sit under it.</summary>
public sealed record NavBandGroup(NavBand Band, IReadOnlyList<NavItem> Items);

/// <summary>
/// One source of truth for which band a row sits in, and the grouping that builds the rail from it.
/// Rows keep the order they were built in, so the rail reads exactly like the list the shell builds.
/// </summary>
public static class NavBands
{
    /// <summary>The bands in rail order.</summary>
    public static IReadOnlyList<NavBand> All { get; } =
        new[] { NavBand.Top, NavBand.Modify, NavBand.Make, NavBand.Ship, NavBand.App };

    /// <summary>The band an area sits in. Every area has one — adding a screen forces a choice here.</summary>
    public static NavBand For(ScreenKey key) => key switch
    {
        ScreenKey.Projects => NavBand.Top,       // the place the app opens on
        ScreenKey.Workspace => NavBand.Modify,   // the four modifying jobs
        ScreenKey.BulkEdit => NavBand.Modify,
        ScreenKey.Build => NavBand.Ship,
        ScreenKey.Mods => NavBand.Ship,
        ScreenKey.Backups => NavBand.Ship,
        ScreenKey.Settings => NavBand.App,
        ScreenKey.Help => NavBand.App,
        _ => NavBand.App,
    };

    /// <summary>The band a row sits in: creating something new has its own band, whatever screen it opens.</summary>
    public static NavBand For(NavItem item) => item.Creates ? NavBand.Make : For(item.Key);

    /// <summary>Groups rail rows under their band, in band order, keeping each band's own order.</summary>
    public static IReadOnlyList<NavBandGroup> Group(IEnumerable<NavItem> items)
    {
        var byBand = items.ToLookup(i => i.Band);
        return All.Where(b => byBand[b].Any())
                  .Select(b => new NavBandGroup(b, byBand[b].ToList()))
                  .ToList();
    }
}
