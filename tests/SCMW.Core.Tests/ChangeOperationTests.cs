using System;
using SCMW.Core;
using SCMW.Core.Editing;

namespace SCMW.Core.Tests;

/// <summary>
/// The <c>set</c> operation, addressed by the file's own path (never by display name). A set
/// recorded against one path applies to that path and to no other — so two files that happen to
/// share a display name can never be confused.
/// </summary>
public class ChangeOperationTests
{
    private static ChangeRecord Set(string path, string field, string value, DateTime? at = null) =>
        new(Guid.NewGuid(), "p1", path, field, null, value, at ?? new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc),
            StatusKind.Editable, AssetPath: path);

    [Fact]
    public void A_set_addressed_to_a_path_applies_to_that_path()
        => Assert.Equal("20",
            AppliedValue.ResolveForPath(new[] { Set("/a/Batteries_ES", "Weight", "20") },
                "/a/Batteries_ES", "Weight", "1"));

    [Fact]
    public void A_set_addressed_to_one_path_does_not_leak_to_another()
        => Assert.Equal("1",
            AppliedValue.ResolveForPath(new[] { Set("/a/Batteries_ES", "Weight", "20") },
                "/b/Batteries_ES", "Weight", "1"));

    [Fact]
    public void Two_files_that_share_a_display_name_keep_their_own_values()
    {
        var records = new[]
        {
            Set("/a/Batteries_ES", "Weight", "100"),
            Set("/b/Batteries_ES", "Weight", "200"),
        };

        Assert.Equal("100", AppliedValue.ResolveForPath(records, "/a/Batteries_ES", "Weight", "0"));
        Assert.Equal("200", AppliedValue.ResolveForPath(records, "/b/Batteries_ES", "Weight", "0"));
    }

    [Fact]
    public void The_newest_set_on_a_path_wins()
    {
        var records = new[]
        {
            Set("/a/Batteries_ES", "Weight", "20", new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc)),
            Set("/a/Batteries_ES", "Weight", "30", new DateTime(2026, 9, 27, 12, 0, 1, DateTimeKind.Utc)),
        };

        Assert.Equal("30", AppliedValue.ResolveForPath(records, "/a/Batteries_ES", "Weight", "1"));
    }

    [Fact] // a record made before paths were kept cannot be matched by path
    public void A_record_with_no_path_is_not_matched_by_path()
    {
        var legacy = new ChangeRecord(Guid.NewGuid(), "p1", "Batteries", "Weight", null, "20",
            new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc), StatusKind.Editable);

        Assert.Equal("1", AppliedValue.ResolveForPath(new[] { legacy }, "/a/Batteries_ES", "Weight", "1"));
    }

    [Fact] // a list change is not a value, even when it names the same path+field
    public void A_list_change_is_never_read_as_a_value()
    {
        var add = Set("/a/Batteries_ES", "Ingredients", "20") with { Operation = ChangeOperation.AddEntry };

        Assert.Equal("1", AppliedValue.ResolveForPath(new[] { add }, "/a/Batteries_ES", "Ingredients", "1"));
    }
}
