using SCMW.App;

namespace SCMW.App.Tests;

public class Ue4ssSwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scmw-ue4ss-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Win64 => Path.Combine(_root, "SCUM", "Binaries", "Win64");
    private string Proxy => Path.Combine(Win64, "dwmapi.dll");
    private string Off => Proxy + ".scmw-off";

    private void MakeUe4ss()
    {
        Directory.CreateDirectory(Win64);
        File.WriteAllText(Proxy, "proxy");
    }

    [Fact]
    public void Clean_turns_the_proxy_off_and_modded_turns_it_back_on()
    {
        MakeUe4ss();
        var ue4ss = new Ue4ssSwitch(_root);

        var off = ue4ss.TurnOff();

        Assert.True(off.Ok, off.Error);
        Assert.False(File.Exists(Proxy));
        Assert.True(File.Exists(Off));
        Assert.True(ue4ss.IsOff);

        var on = ue4ss.TurnOn();

        Assert.True(on.Ok, on.Error);
        Assert.True(File.Exists(Proxy));
        Assert.False(File.Exists(Off));
        Assert.False(ue4ss.IsOff);
    }

    [Fact]
    public void Without_ue4ss_installed_both_ways_are_harmless()
    {
        Directory.CreateDirectory(Win64);
        var ue4ss = new Ue4ssSwitch(_root);

        Assert.True(ue4ss.TurnOff().Ok);
        Assert.True(ue4ss.TurnOn().Ok);
        Assert.False(File.Exists(Proxy));
        Assert.False(File.Exists(Off));
    }

    [Fact]
    public void Repeating_a_switch_does_not_lose_the_proxy()
    {
        MakeUe4ss();
        var ue4ss = new Ue4ssSwitch(_root);

        Assert.True(ue4ss.TurnOff().Ok);
        Assert.True(ue4ss.TurnOff().Ok);
        Assert.True(ue4ss.TurnOn().Ok);
        Assert.True(ue4ss.TurnOn().Ok);
        Assert.True(File.Exists(Proxy));
        Assert.False(File.Exists(Off));
    }

    [Fact]
    public void An_interrupted_restore_keeps_the_live_proxy()
    {
        MakeUe4ss();
        var ue4ss = new Ue4ssSwitch(_root);
        Assert.True(ue4ss.TurnOff().Ok);

        File.WriteAllText(Proxy, "fresh");        // both files present after an interrupted restore

        Assert.True(ue4ss.TurnOn().Ok);
        Assert.True(File.Exists(Proxy));
        Assert.False(File.Exists(Off));
        Assert.Equal("fresh", File.ReadAllText(Proxy));
    }

    [Fact]
    public void A_locked_proxy_is_reported_plainly()
    {
        MakeUe4ss();
        using var hold = new FileStream(Proxy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ue4ss = new Ue4ssSwitch(_root);

        var result = ue4ss.TurnOff();

        Assert.False(result.Ok);
        Assert.Contains("Close SCUM", result.Error);
        Assert.True(File.Exists(Proxy));
    }

    [Fact]
    public void A_root_that_does_not_exist_is_not_available_but_never_crashes()
    {
        var ue4ss = new Ue4ssSwitch(_root);

        Assert.True(ue4ss.TurnOff().Ok);
        Assert.True(ue4ss.TurnOn().Ok);
        Assert.False(ue4ss.IsOff);
    }
}
