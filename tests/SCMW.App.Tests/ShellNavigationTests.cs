namespace SCMW.App.Tests;

/// <summary>
/// The shell's navigation model: the three rail bands (Make · Ship · App) and how the
/// rail rows group under them. See the design, §5 Navigation.
/// </summary>
public class ShellNavigationTests
{
    [Fact]
    public void Bands_run_Make_Ship_App_in_order()
        => Assert.Equal(new[] { NavBand.Make, NavBand.Ship, NavBand.App }, NavBands.All);

    [Theory]
    [InlineData(ScreenKey.Workspace, NavBand.Make)]
    [InlineData(ScreenKey.Projects, NavBand.Make)]
    [InlineData(ScreenKey.Build, NavBand.Ship)]
    [InlineData(ScreenKey.Mods, NavBand.Ship)]
    [InlineData(ScreenKey.Backups, NavBand.Ship)]
    [InlineData(ScreenKey.Settings, NavBand.App)]
    [InlineData(ScreenKey.Help, NavBand.App)]
    public void Each_screen_sits_in_its_band(ScreenKey key, NavBand band)
        => Assert.Equal(band, NavBands.For(key));

    [Fact]
    public void A_rail_row_knows_its_band()
        => Assert.Equal(NavBand.Ship, new NavItem(ScreenKey.Build, "Build").Band);

    [Fact]
    public void Screens_group_under_their_band_in_band_order()
    {
        var items = new[]
        {
            new NavItem(ScreenKey.Help, "Help"),
            new NavItem(ScreenKey.Projects, "Projects"),
            new NavItem(ScreenKey.Build, "Build"),
            new NavItem(ScreenKey.Settings, "Settings"),
            new NavItem(ScreenKey.Mods, "Mods"),
            new NavItem(ScreenKey.Backups, "Backups"),
            new NavItem(ScreenKey.Workspace, "Workspace"),
        };

        var groups = NavBands.Group(items);

        Assert.Equal(new[] { NavBand.Make, NavBand.Ship, NavBand.App },
                     groups.Select(g => g.Band));
        Assert.Equal(new[] { ScreenKey.Workspace, ScreenKey.Projects },
                     groups[0].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Build, ScreenKey.Mods, ScreenKey.Backups },
                     groups[1].Items.Select(i => i.Key));
        Assert.Equal(new[] { ScreenKey.Settings, ScreenKey.Help },
                     groups[2].Items.Select(i => i.Key));
    }
}
