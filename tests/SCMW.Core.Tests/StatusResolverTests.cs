// tests/SCMW.Core.Tests/StatusResolverTests.cs
// Task 3: the resolve matrix, and Review Focus 4 - Editable is impossible without a
// recorded tested fact.
using SCMW.Core.Knowledge;

namespace SCMW.Core.Tests;

public class StatusResolverTests
{
    [Theory] // the full matrix
    [InlineData(WriteCapability.Writable, true, ResolvedStatus.Editable)]
    [InlineData(WriteCapability.Writable, false, ResolvedStatus.Limited)]
    [InlineData(WriteCapability.NotWritable, true, ResolvedStatus.ReadOnly)]
    [InlineData(WriteCapability.NotWritable, false, ResolvedStatus.ReadOnly)]
    [InlineData(WriteCapability.Unknown, true, ResolvedStatus.Unknown)]
    [InlineData(WriteCapability.Unknown, false, ResolvedStatus.Unknown)]
    public void Resolve_covers_the_full_matrix(
        WriteCapability capability, bool hasTestedFact, ResolvedStatus expected)
    {
        Assert.Equal(expected, StatusResolver.Resolve(capability, hasTestedFact));
    }

    [Fact] // Review Focus 4: a writable field with no fact is Limited, never Editable
    public void A_writable_field_without_a_tested_fact_is_limited_never_editable()
    {
        var resolved = StatusResolver.Resolve(WriteCapability.Writable, hasTestedFact: false);

        Assert.Equal(ResolvedStatus.Limited, resolved);
        Assert.NotEqual(ResolvedStatus.Editable, resolved);
    }

    [Fact] // and no capability at all can reach Editable without a fact
    public void Editable_is_impossible_without_a_tested_fact_for_every_capability()
    {
        foreach (var capability in Enum.GetValues<WriteCapability>())
        {
            var resolved = StatusResolver.Resolve(capability, hasTestedFact: false);
            Assert.NotEqual(ResolvedStatus.Editable, resolved);
        }
    }

    [Theory] // Task 4: the one central ResolvedStatus -> StatusKind mapping is exhaustive
    [InlineData(ResolvedStatus.Editable, StatusKind.Editable)]
    [InlineData(ResolvedStatus.Limited, StatusKind.Limited)]
    [InlineData(ResolvedStatus.ReadOnly, StatusKind.ReadOnly)]
    [InlineData(ResolvedStatus.Unknown, StatusKind.Unknown)]
    public void ToStatusKind_maps_every_resolved_status(ResolvedStatus status, StatusKind expected) =>
        Assert.Equal(expected, StatusResolver.ToStatusKind(status));

    [Fact] // Task 4 / Review Focus 4: an unrecognised enum value must never fall through to Editable
    public void ToStatusKind_never_falls_through_to_editable()
    {
        var unknownValue = (ResolvedStatus)999;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => StatusResolver.ToStatusKind(unknownValue));
        Assert.Equal("status", ex.ParamName);
    }

    [Fact] // Task 4 / Review Focus 4: the composed UI path is never Editable without evidence
    public void Composed_path_is_never_editable_without_a_tested_fact()
    {
        foreach (var name in new[] { "MaxStackSize", "Damage", "ItemClass", "MysteryField" })
        {
            var status = StatusResolver.ToStatusKind(
                StatusResolver.For(name, valueTypeHint: null, hasTestedFact: false));
            Assert.NotEqual(StatusKind.Editable, status);
        }
    }
}
