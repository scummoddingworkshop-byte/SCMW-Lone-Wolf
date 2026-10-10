using SCMW.App;

namespace SCMW.App.Tests;

public class AppInfoTests
{
    [Fact]
    public void The_support_link_is_a_web_address_for_the_ko_fi_page()
    {
        Assert.StartsWith("https://", AppInfo.SupportUrl, StringComparison.Ordinal);
        Assert.Contains("ko-fi.com/scmwlonewolf", AppInfo.SupportUrl, StringComparison.OrdinalIgnoreCase);
    }
}
