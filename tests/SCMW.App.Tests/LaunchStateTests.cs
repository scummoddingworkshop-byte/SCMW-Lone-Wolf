using Avalonia.Headless.XUnit;

namespace SCMW.App.Tests;

public class LaunchStateTests
{
    [AvaloniaFact]
    public void Launch_note_explains_when_launching_is_unavailable()
    {
        var vm = new ShellViewModel(new NavigationHost());

        Assert.False(vm.CanLaunch);
        Assert.True(vm.ShowLaunchNote);
        Assert.Equal("Set your SCUM folder in Settings to launch.", vm.LaunchNote);
    }
}
