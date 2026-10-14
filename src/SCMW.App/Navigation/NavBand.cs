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

    /// <summary>The rail rows in the order the design lists them. Workspace leads Make; the kind
    /// families (Items, Base building, Recipes) and Home follow it until Phase 2's Workspace absorbs them.</summary>
    private static readonly ScreenKey[] RailOrder =
    {
        ScreenKey.Workspace,
        ScreenKey.Home, ScreenKey.Projects, ScreenKey.Items, ScreenKey.BaseBuilding, ScreenKey.Recipes,
        ScreenKey.Preview, ScreenKey.Build, ScreenKey.Mods, ScreenKey.Backups,
        ScreenKey.Settings, ScreenKey.Help,
    };

    /// <summary>The band an area sits in. Every area has one — adding a screen forces a choice here.</summary>
    public static NavBand For(ScreenKey key) => key switch
    {
        ScreenKey.Workspace => NavBand.Make,
        ScreenKey.Projects => NavBand.Make,
        // The kinds (Items, Base building, Recipes) and Home are make-work: they fold into the
        // Workspace and its chooser, so they live in Make until they are retired.
        ScreenKey.Home => NavBand.Make,
        ScreenKey.Items => NavBand.Make,
        ScreenKey.BaseBuilding => NavBand.Make,
        ScreenKey.Recipes => NavBand.Make,
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
