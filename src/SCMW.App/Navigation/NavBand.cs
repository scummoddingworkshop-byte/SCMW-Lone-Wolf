using System;
using System.Collections.Generic;
using System.Linq;

namespace SCMW.App;

/// <summary>The three labelled bands of the shell rail, top to bottom (design §5).</summary>
public enum NavBand
{
    Make,
    Ship,
    App,
}

/// <summary>One band and the rail rows that sit under it.</summary>
public sealed record NavBandGroup(NavBand Band, IReadOnlyList<NavItem> Items);

/// <summary>
/// One source of truth for which band an area sits in, and the grouping that builds the rail
/// from it. Rows are ordered as the design lists them; empty bands are dropped.
/// </summary>
public static class NavBands
{
    /// <summary>The bands in rail order.</summary>
    public static IReadOnlyList<NavBand> All { get; } =
        new[] { NavBand.Make, NavBand.Ship, NavBand.App };

    /// <summary>The rail rows in the order the design lists them. Workspace leads Make.</summary>
    private static readonly ScreenKey[] RailOrder =
    {
        ScreenKey.Workspace,
        ScreenKey.BulkEdit,
        ScreenKey.Home, ScreenKey.Projects,
        ScreenKey.Preview, ScreenKey.Build, ScreenKey.Mods, ScreenKey.Backups,
        ScreenKey.Settings, ScreenKey.Help,
    };

    /// <summary>The band an area sits in. Every area has one — adding a screen forces a choice here.</summary>
    public static NavBand For(ScreenKey key) => key switch
    {
        ScreenKey.Workspace => NavBand.Make,
        ScreenKey.BulkEdit => NavBand.Make,
        ScreenKey.Projects => NavBand.Make,
        ScreenKey.Home => NavBand.Make,
        ScreenKey.Preview => NavBand.Ship,
        ScreenKey.Build => NavBand.Ship,
        ScreenKey.Mods => NavBand.Ship,
        ScreenKey.Backups => NavBand.Ship,
        ScreenKey.Settings => NavBand.App,
        ScreenKey.Help => NavBand.App,
        _ => NavBand.App,
    };

    /// <summary>Groups rail rows under their band, in band order, each band in the design's row order.</summary>
    public static IReadOnlyList<NavBandGroup> Group(IEnumerable<NavItem> items)
    {
        var byBand = items.ToLookup(i => i.Band);
        return All.Where(b => byBand[b].Any())
                  .Select(b => new NavBandGroup(b, byBand[b].OrderBy(i => Rank(i.Key)).ToList()))
                  .ToList();
    }

    /// <summary>A row's position in the design's rail order; unknown areas sort last.</summary>
    private static int Rank(ScreenKey key)
    {
        var index = Array.IndexOf(RailOrder, key);
        return index < 0 ? int.MaxValue : index;
    }
}
