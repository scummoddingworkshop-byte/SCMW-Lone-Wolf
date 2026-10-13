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

    private static ChangeRecord Times(string path, string field, string factor, DateTime? at = null) =>
        new(Guid.NewGuid(), "p1", path, field, null, factor,
            at ?? new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc),
            StatusKind.Editable, Operation: ChangeOperation.Multiply, AssetPath: path);

    [Fact]
    public void A_multiply_addressed_to_a_path_scales_the_game_value()
        => Assert.Equal("20",
            AppliedValue.ResolveForPath(new[] { Times("/a/Batteries_ES", "Weight", "2") },
                "/a/Batteries_ES", "Weight", "10"));

    [Fact]
    public void A_multiply_addressed_to_one_path_does_not_leak_to_another()
        => Assert.Equal("10",
            AppliedValue.ResolveForPath(new[] { Times("/a/Batteries_ES", "Weight", "2") },
                "/b/Batteries_ES", "Weight", "10"));

    [Fact] // "half" — a fraction halves the value
    public void A_multiply_by_a_fraction_halves_the_value()
        => Assert.Equal("5",
            AppliedValue.ResolveForPath(new[] { Times("/a/Batteries_ES", "Weight", "0.5") },
                "/a/Batteries_ES", "Weight", "10"));

    [Fact] // ×0 is allowed and gives a clean 0 (never "-0")
    public void A_multiply_by_zero_gives_a_clean_zero()
        => Assert.Equal("0",
            AppliedValue.ResolveForPath(new[] { Times("/a/Batteries_ES", "Weight", "0") },
                "/a/Batteries_ES", "Weight", "10"));

    [Fact] // a big factor stays a plain number, not "1E+13"
    public void A_large_multiply_stays_a_plain_number()
        => Assert.Equal("10000000000000",
            AppliedValue.ResolveForPath(new[] { Times("/a/Batteries_ES", "Weight", "1000000000000") },
                "/a/Batteries_ES", "Weight", "10"));

    [Fact] // the newest change wins: a later multiply overrides an earlier set
    public void A_newer_multiply_wins_over_an_older_set()
    {
        var records = new[]
        {
            Set("/a/Batteries_ES", "Weight", "999"),
            Times("/a/Batteries_ES", "Weight", "2"),
        };

        Assert.Equal("20", AppliedValue.ResolveForPath(records, "/a/Batteries_ES", "Weight", "10"));
    }

    [Fact] // and a later set overrides an earlier multiply
    public void A_newer_set_wins_over_an_older_multiply()
    {
        var records = new[]
        {
            Times("/a/Batteries_ES", "Weight", "2"),
            Set("/a/Batteries_ES", "Weight", "999"),
        };

        Assert.Equal("999", AppliedValue.ResolveForPath(records, "/a/Batteries_ES", "Weight", "10"));
    }

    [Fact] // a value the app cannot read as a number is left exactly as it was
    public void A_multiply_of_a_non_number_leaves_it_alone()
        => Assert.Equal("abc",
            AppliedValue.ResolveForPath(new[] { Times("/a/Batteries_ES", "Weight", "2") },
                "/a/Batteries_ES", "Weight", "abc"));

    [Fact] // a result too large to represent is refused: the game value stays
    public void A_multiply_whose_result_overflows_leaves_the_value_alone()
        => Assert.Equal("1e308",
            AppliedValue.ResolveForPath(new[] { Times("/a/Batteries_ES", "Weight", "2") },
                "/a/Batteries_ES", "Weight", "1e308"));
}
