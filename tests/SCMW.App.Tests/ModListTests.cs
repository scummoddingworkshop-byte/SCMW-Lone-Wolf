using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class ModListTests
{
    [AvaloniaFact]
    public void Adding_a_mod_marks_it_external()
    {
        var list = new ModList();

        var mod = list.Add("SomeMod.pak");

        Assert.True(mod.IsExternal);
        Assert.Equal("External", mod.Badge);
        Assert.Single(list.Mods);
        Assert.True(mod.IsOn);
    }

    [AvaloniaFact]
    public void Installing_adds_a_mod_built_here_and_refreshes_it_the_second_time()
    {
        var list = new ModList();

        var first = list.Install("My first mod");
        Assert.False(first.IsExternal);
        Assert.Equal("SCMW", first.Badge);
        Assert.Single(list.Mods);

        first.IsOn = false;
        var again = list.Install("My first mod");

        Assert.Single(list.Mods);            // not duplicated
        Assert.Same(first, again);
        Assert.True(again.IsOn);             // installing switches it back on
    }

    [AvaloniaFact]
    public void Removing_drops_the_mod()
    {
        var list = new ModList();
        var mod = list.Add("SomeMod.pak");

        list.Remove(mod);

        Assert.Empty(list.Mods);
    }

    [AvaloniaFact]
    public void The_summary_counts_what_is_installed_and_what_is_on()
    {
        var list = new ModList();
        list.Install("My first mod");
        var other = list.Add("SomeMod.pak");
        list.Add("Another.pak");

        Assert.Equal("3 mods installed · 3 switched on.", list.Summary);

        other.IsOn = false;

        Assert.Equal(3, list.InstalledCount);
        Assert.Equal(2, list.OnCount);
        Assert.Equal("3 mods installed · 2 switched on.", list.Summary);
    }

    [AvaloniaFact]
    public void The_summary_is_grammatical_for_one_mod()
    {
        var list = new ModList();
        list.Add("Only.pak");

        Assert.Equal("1 mod installed · 1 switched on.", list.Summary);
    }
}
