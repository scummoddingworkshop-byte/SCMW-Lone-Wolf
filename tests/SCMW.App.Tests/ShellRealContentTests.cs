using Avalonia.Headless.XUnit;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.App.Tests;

public class ShellRealContentTests
{
    /// <summary>A ready source with two real items; no game, no disk.</summary>
    private sealed class ReadySource : IContentSource
    {
        private sealed class NoIcons : IIconCache
        {
            public string IconsDirectory => "";
            public string? GetIconPath(string assetId) => null;
            public int Count => 0;
            public long TotalBytes => 0;
            public void Clear() { }
        }

        public ContentSourceStatus Status => new(ContentSourceState.Ready, "mounted", "25247492");
        public ScanInfo? LastScan => null;
        public IIconCache Icons { get; } = new NoIcons();
        public IReadOnlyList<string> Categories => new[] { "Weapons", "Food" };
        public void Configure(string p, string? k, string? b) { }
        public Task<ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default) =>
            Task.FromResult(Status);
        public Task<IReadOnlyList<AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetSummary>>(new[]
            {
                new AssetSummary("SCUM/.../Improvised_Bow_ES", "Improvised Bow", "Weapons",
                    StatusKind.Unknown, 0, "SCUM/.../Improvised_Bow_ES.uasset", null),
                new AssetSummary("SCUM/.../CannedSpaghetti_ES", "Canned Spaghetti", "Food",
                    StatusKind.Unknown, 0, "SCUM/.../CannedSpaghetti_ES.uasset", null),
            });
        public Task<IReadOnlyList<AssetField>> FieldsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AssetField>>(Array.Empty<AssetField>());
        public Task<IconExtractionProgress> ExtractAllIconsAsync(IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task<IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<IconExtractionProgress>? p = null, CancellationToken ct = default) =>
            Task.FromResult(new IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task A_shell_over_a_ready_source_shows_real_items_and_no_sample_note()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var source = new ReadySource();
            var vm = new ShellViewModel(new NavigationHost(), projects: projects, contentSource: source);

            await vm.Items.LoadFromSourceAsync();

            Assert.True(vm.Items.IsUsingRealItems);
            Assert.Contains(vm.Items.VisibleItems, i => i.Name == "Improvised Bow");
            Assert.DoesNotContain(vm.Items.VisibleItems, i => i.Name == "Duct Tape");
            Assert.Equal("", vm.Items.SourceNote);
        }
        finally { TestProjects.Cleanup(root); }
    }
}

