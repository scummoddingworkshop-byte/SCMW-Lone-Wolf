// tests/SCMW.Core.Tests/ContentContractTests.cs
// Pins the shape of the Core content contracts (Task 3): records round-trip,
// ContentSourceState has exactly the five agreed members, and a fake
// IContentSource can implement the interface as specified.
using System.Threading;
using System.Threading.Tasks;
using SCMW.Core;
using SCMW.Core.Content;
using Xunit;

namespace SCMW.Core.Tests;

public class ContentContractTests
{
    [Fact]
    public void AssetSummary_round_trips_all_members()
    {
        var summary = new AssetSummary(
            Id: "asset-1",
            Name: "Rifle_556",
            Category: "Weapons",
            Overall: StatusKind.Editable,
            EditableCount: 7,
            TechnicalPath: "SCUM/Content/Weapons/Rifle_556.uasset",
            IconKey: "iconweapons");

        Assert.Equal("asset-1", summary.Id);
        Assert.Equal("Rifle_556", summary.Name);
        Assert.Equal("Weapons", summary.Category);
        Assert.Equal(StatusKind.Editable, summary.Overall);
        Assert.Equal(7, summary.EditableCount);
        Assert.Equal("SCUM/Content/Weapons/Rifle_556.uasset", summary.TechnicalPath);
        Assert.Equal("iconweapons", summary.IconKey);

        var noIcon = summary with { IconKey = null };
        Assert.Null(noIcon.IconKey);
        Assert.Equal(summary, summary with { });
    }

    [Fact]
    public void AssetField_round_trips_all_members()
    {
        var field = new AssetField(
            FriendlyName: "Damage",
            TechnicalName: "BaseDamage",
            Status: StatusKind.Limited,
            CurrentValue: "41.5");

        Assert.Equal("Damage", field.FriendlyName);
        Assert.Equal("BaseDamage", field.TechnicalName);
        Assert.Equal(StatusKind.Limited, field.Status);
        Assert.Equal("41.5", field.CurrentValue);
        Assert.Null(field.ValueTypeHint);   // optional: absent unless a source supplies it
        Assert.Equal(field, field with { });
    }

    [Fact]
    public void ContentSourceState_has_exactly_five_members()
    {
        Assert.Equal(
            new[]
            {
                ContentSourceState.NotConfigured,
                ContentSourceState.NoKey,
                ContentSourceState.Scanning,
                ContentSourceState.Ready,
                ContentSourceState.Error,
            },
            Enum.GetValues<ContentSourceState>());
    }

    [Fact]
    public void ContentSourceStatus_round_trips_all_members()
    {
        var status = new ContentSourceStatus(
            ContentSourceState.Ready, "3 packs indexed", "build-42");

        Assert.Equal(ContentSourceState.Ready, status.State);
        Assert.Equal("3 packs indexed", status.Message);
        Assert.Equal("build-42", status.GameBuildId);

        var unknown = new ContentSourceStatus(ContentSourceState.NotConfigured, "No pak configured.", null);
        Assert.Null(unknown.GameBuildId);
    }

    [Fact]
    public void ScanInfo_round_trips_all_members()
    {
        var info = new ScanInfo(new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc), 12345, "build-42");

        Assert.Equal(12345, info.ItemCount);
        Assert.Equal("build-42", info.BuildId);
        Assert.NotNull(info.ScannedAtUtc);
        Assert.Equal(info, info with { });
    }

    [Fact]
    public async Task FakeContentSource_satisfies_the_interface()
    {
        IContentSource source = new FakeContentSource();

        Assert.Equal(ContentSourceState.NotConfigured, source.Status.State);

        var scan = await source.ScanAsync();
        Assert.Equal(ContentSourceState.Ready, scan.State);
        Assert.Equal("fake build", scan.GameBuildId);
        Assert.Equal(new[] { "Weapons" }, source.Categories);

        var results = await source.SearchAsync("rifle", "Weapons");
        Assert.Single(results);
        Assert.Equal("asset-1", results[0].Id);

        var fields = await source.FieldsAsync("asset-1");
        Assert.Single(fields);
        Assert.Equal("BaseDamage", fields[0].TechnicalName);
    }

    private sealed class FakeContentSource : IContentSource
    {
        public ContentSourceStatus Status { get; private set; } =
            new(ContentSourceState.NotConfigured, "Not configured.", null);

        public ScanInfo? LastScan => null;

        public IIconCache Icons { get; } = new FakeIconCache();

        public void Configure(string pakPath, string? aesKey, string? knownBuildId)
            => Status = new ContentSourceStatus(
                ContentSourceState.Ready, $"Configured {pakPath}.", knownBuildId);

        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        {
            Status = new ContentSourceStatus(ContentSourceState.Ready, "Scan complete.", "fake build");
            return Task.FromResult(Status);
        }

        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string query, string? category,
            int skip = 0, int take = 100, CancellationToken ct = default)
        {
            IReadOnlyList<AssetSummary> items = new[]
            {
                new AssetSummary("asset-1", "Rifle_556", "Weapons",
                    StatusKind.Editable, 1, "SCUM/Content/Weapons/Rifle_556.uasset", null),
            };
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<AssetField>> FieldsAsync(string assetId, CancellationToken ct = default)
        {
            IReadOnlyList<AssetField> items = new[]
            {
                new AssetField("Damage", "BaseDamage", StatusKind.Editable, "41.5"),
            };
            return Task.FromResult(items);
        }

        public IReadOnlyList<string> Categories => new[] { "Weapons" };

        public Task<IconExtractionProgress> ExtractAllIconsAsync(
            IProgress<IconExtractionProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new IconExtractionProgress(1, 1, 0, 0));
            return Task.FromResult(new IconExtractionProgress(1, 1, 0, 0));
        }

        public Task<IconExtractionProgress> ExtractIconsAsync(
            IEnumerable<string> assetIds,
            IProgress<IconExtractionProgress>? progress = null, CancellationToken ct = default)
            => ExtractAllIconsAsync(progress, ct);

        public Task ClearIconsAsync(CancellationToken ct = default)
        {
            Icons.Clear();
            return Task.CompletedTask;
        }
    }

    /// <summary>Minimal in-memory cache so the interface fake stays self-contained.</summary>
    private sealed class FakeIconCache : IIconCache
    {
        public string IconsDirectory => Path.GetTempPath();

        public string? GetIconPath(string assetId) => null;

        public int Count => 0;

        public long TotalBytes => 0;

        public void Clear() { }
    }
}
