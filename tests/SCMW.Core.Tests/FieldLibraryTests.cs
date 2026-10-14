// tests/SCMW.Core.Tests/FieldLibraryTests.cs
// Task 2.1: the field library - the single source of truth for what a field IS
// (its type, unit, plain meaning, risk, and whether we can write it).
using SCMW.Core.Knowledge;
using Xunit;

namespace SCMW.Core.Tests;

public class FieldLibraryTests
{
    private const string TwoFields = """
    [
      { "kind": "Item", "name": "Weight", "label": "Weight", "type": "Number",
        "unit": "kg", "risk": "Low", "writable": true,
        "meaning": "How heavy the item is." },
      { "kind": "Item", "name": "MaxStackSize", "label": "Max Stack", "type": "Number",
        "unit": "items", "risk": "Medium", "writable": true,
        "meaning": "How many can stack in one slot." }
    ]
    """;

    [Fact]
    public void Parse_loads_a_definition_and_lookup_returns_it()
    {
        var lib = FieldLibraryLoader.Parse(TwoFields);

        var def = lib.Lookup("Item", "Weight");

        Assert.NotNull(def);
        Assert.Equal("Weight", def!.Name);
        Assert.Equal("Weight", def.Label);
        Assert.Equal(FieldType.Number, def.Type);
        Assert.Equal("kg", def.Unit);
        Assert.Equal(Risk.Low, def.Risk);
        Assert.True(def.Writable);
        Assert.Equal("How heavy the item is.", def.Meaning);
    }

    [Fact]
    public void An_addable_field_is_flagged_and_others_default_to_not_addable()
    {
        const string json = """
        [
          { "kind": "Items", "name": "_autoDestructTime", "label": "Auto-destruct time",
            "type": "Number", "risk": "Low", "writable": true, "addable": true, "meaning": "x" },
          { "kind": "Items", "name": "Weight", "label": "Weight",
            "type": "Number", "risk": "Low", "writable": true, "meaning": "y" }
        ]
        """;
        var lib = FieldLibraryLoader.Parse(json);

        Assert.True(lib.Lookup("Items", "_autoDestructTime")!.Addable);
        Assert.False(lib.Lookup("Items", "Weight")!.Addable);
    }

    [Fact] // only the fields we have decided are safe to add carry the flag
    public void The_shipped_library_marks_only_the_auto_destruct_timer_addable()
    {
        var lib = FieldLibraryLoader.LoadDefault();

        Assert.True(lib.Lookup("Items", "_autoDestructTime")!.Addable);
        Assert.False(lib.Lookup("Items", "Weight")!.Addable);
        Assert.False(lib.Lookup("Items", "ShelfLife")!.Addable);
    }

    [Fact]
    public void Lookup_returns_null_for_an_unknown_field()
    {
        var lib = FieldLibraryLoader.Parse(TwoFields);

        Assert.Null(lib.Lookup("Item", "NoSuchField"));
    }

    [Fact]
    public void Lookup_returns_null_for_a_field_of_another_kind()
    {
        var lib = FieldLibraryLoader.Parse(TwoFields);

        Assert.Null(lib.Lookup("Recipe", "Weight"));
    }

    [Theory]
    [InlineData("item", "weight")]
    [InlineData("ITEM", "WEIGHT")]
    public void Lookup_ignores_case(string kind, string field)
    {
        var lib = FieldLibraryLoader.Parse(TwoFields);

        Assert.NotNull(lib.Lookup(kind, field));
    }

    [Fact]
    public void ForKind_returns_the_kinds_fields_in_file_order()
    {
        var lib = FieldLibraryLoader.Parse(TwoFields);

        Assert.Equal(new[] { "Weight", "MaxStackSize" },
            lib.ForKind("Item").Select(f => f.Name).ToArray());
    }

    [Fact]
    public void ForKind_returns_empty_for_a_kind_with_no_fields()
    {
        var lib = FieldLibraryLoader.Parse(TwoFields);

        Assert.Empty(lib.ForKind("Recipe"));
    }

    [Fact]
    public void Parse_reads_unrecognised_type_and_risk_as_unknown()
    {
        var lib = FieldLibraryLoader.Parse("""
        [ { "kind": "Item", "name": "Odd", "label": "Odd", "type": "Wibble",
            "risk": "Nope", "unit": "", "writable": false, "meaning": "" } ]
        """);

        var def = lib.Lookup("Item", "Odd");

        Assert.NotNull(def);
        Assert.Equal(FieldType.Unknown, def!.Type);
        Assert.Equal(Risk.Unknown, def.Risk);
        Assert.False(def.Writable);
    }

    [Fact]
    public void Parse_ignores_entries_with_no_kind_or_no_name()
    {
        var lib = FieldLibraryLoader.Parse("""
        [ { "name": "NoKind", "label": "x" },
          { "kind": "Item", "label": "NoName" },
          { "kind": "Item", "name": "Good", "label": "Good" } ]
        """);

        Assert.Null(lib.Lookup("Item", "NoName"));
        Assert.Null(lib.Lookup("Item", "NoKind"));
        Assert.NotNull(lib.Lookup("Item", "Good"));
    }

    [Fact]
    public void LoadDefault_loads_the_shipped_library()
    {
        var lib = FieldLibraryLoader.LoadDefault();

        Assert.NotNull(lib.Lookup("Items", "Weight"));
    }

    [Fact] // Review Focus 5: a string-table key is not presentable as editable words
    public void A_string_table_key_field_is_not_writable()
    {
        var lib = FieldLibraryLoader.LoadDefault();

        Assert.False(lib.Lookup("Items", "Caption")!.Writable);
        Assert.False(lib.Lookup("Items", "Description")!.Writable);
    }

    [Fact] // but a real tag field (the game matches it exactly) stays writable
    public void A_tag_field_stays_writable()
    {
        var lib = FieldLibraryLoader.LoadDefault();

        Assert.True(lib.Lookup("Items", "GridInventorySortGroup.TagName")!.Writable);
    }
}
