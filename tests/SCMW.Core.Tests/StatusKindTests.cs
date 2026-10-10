// tests/SCMW.Core.Tests/StatusKindTests.cs
// Trivial sanity test replacing the Task 1 placeholder.
using Xunit;

namespace SCMW.Core.Tests;

public class StatusKindTests
{
    [Fact]
    public void StatusKind_has_exactly_four_members()
    {
        Assert.Equal(4, Enum.GetValues<SCMW.Core.StatusKind>().Length);
    }
}
