using SCMW.App;

namespace SCMW.App.Tests;

public class SteamReadinessTests
{
    [Fact]
    public void Steam_counts_as_ready_only_when_its_marker_is_a_positive_number()
    {
        // Steam writes ActiveProcess\ActiveUser as a DWORD: 0 means "running but not signed in",
        // which is exactly the cold-start state where -applaunch used to fail.
        Assert.False(SystemProcessLauncher.IsSignedIn(null));
        Assert.False(SystemProcessLauncher.IsSignedIn(0));
        Assert.True(SystemProcessLauncher.IsSignedIn(1));
        Assert.True(SystemProcessLauncher.IsSignedIn((long)12345));
    }
}
