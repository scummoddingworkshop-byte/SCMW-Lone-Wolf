// tests/SCMW.Core.Tests/AssetFieldHintTests.cs
// Task 4: AssetField carries the UE property type hint the honesty engine needs.
using SCMW.Core.Content;

namespace SCMW.Core.Tests;

public class AssetFieldHintTests
{
    [Fact]
    public void AssetField_round_trips_the_value_type_hint()
    {
        var field = new AssetField("Max Stack Size", "MaxStackSize",
            StatusKind.Limited, "5", ValueTypeHint: "IntProperty");

        Assert.Equal("IntProperty", field.ValueTypeHint);

        // The hint is optional: a field built without one still works and stays honest
        // (no hint means the honesty engine falls back to the name alone).
        var withoutHint = new AssetField("Damage", "Damage", StatusKind.Unknown, "24");
        Assert.Null(withoutHint.ValueTypeHint);
    }
}
