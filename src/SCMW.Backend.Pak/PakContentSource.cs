// src/SCMW.Backend.Pak/PakContentSource.cs
// The real SCUM content source: mounts the game's pak, enumerates item
// definitions, and reads their configured fields through CUE4Parse.
//
// Item filter (authoritative heuristic — see docs/superpowers/notes/
// plan2-spike-findings.md): a .uasset Blueprint package under
// SCUM/Content/ConZ_Files/Items/, excluding the non-item subtrees
// Item_Icons/ SpawnerPresets/ SpawnerPresets2/ StringTables/ Tags/ and
// filenames prefixed SM_ MI_ M_ SK_ T_ MF_ PM_ S_ BP_ ABP_. The authoritative
// check (the package exports a UBlueprintGeneratedClass named <AssetName>_C)
// is not run over every candidate during the scan because loading ~6,788
// packages up-front is prohibitive; FieldsAsync applies that check implicitly
// by loading the class default object (Default__<AssetName>_C) on demand.
//
// Search is served from the SQLite asset index (IAssetIndex) when a db path is
// configured (the production path); the in-memory list is kept only as the
// fallback for unit tests that construct the source with no index/db path.
using System.Globalization;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using SCMW.Core.Content;
using SCMW.Core;
using SCMW.Core.Logging;

namespace SCMW.Backend.Pak;

public sealed class PakContentSource : IContentSource
{
    private const string ItemsRoot = "SCUM/Content/ConZ_Files/Items/";

    // Base-building elements (the well and its relatives) live here; their values sit on the
    // blueprint's default object, so the same reading machinery serves them.
    private const string BaseElementsRoot = "SCUM/Content/ConZ_Files/BaseBuilding/BaseElements/";

    // Loot & spawn (Task 2.6): the folders the item list deliberately skips (see
    // ExcludedSubfolders) are read here as their own family. Two live under Items/ and stay
    // skipped as items; the fish/animal presets live under Characters/. The reader's job is
    // unchanged — this is just a third place to look, using the same machinery.
    private static readonly string[] SpawnerRoots =
    {
        "SCUM/Content/ConZ_Files/Items/SpawnerPresets/",
        "SCUM/Content/ConZ_Files/Items/SpawnerPresets2/",
        "SCUM/Content/ConZ_Files/Characters/SpawnerPresets/",
    };

    // Shown (and returned as the scan Error) when the pak enumerates but its item
    // packages cannot actually be decrypted — a missing/rotated/wrong AES key.
    private const string WrongKeyMessage =
        "SCUM's files couldn't be decrypted — your AES key may be missing, wrong, or outdated. Check it in Settings.";

    // SearchAsync safety cap, shared with AssetIndex via ContentLimits.MaxTake so
    // the two can't drift.
    private const int MaxTake = ContentLimits.MaxTake;

    // How many candidates the wrong-key probe samples. A single candidate can be a
    // heuristic false positive (its path looks like an item but its class/CDO does
    // not load), so a handful spread across the list is probed instead of trusting
    // items[0] alone. Kept small: loading ~6.8k packages up-front is prohibitive.
    private const int ProbeSampleSize = 10;

    private static readonly string[] ExcludedSubfolders =
    {
        "Item_Icons/", "SpawnerPresets/", "SpawnerPresets2/", "StringTables/", "Tags/",
    };

    private static readonly string[] ExcludedPrefixes =
    {
        "SM_", "MI_", "M_", "SK_", "T_", "MF_", "PM_", "S_", "BP_", "ABP_",
    };

    private readonly IPakMount _mount;
    private readonly IAssetIndex? _index;
    private readonly string? _dbPath;
    private readonly IIconCache _icons;

    // The shared source is used by both Settings (key probe + rescan) and Assets
    // (scan on open). A single gate serializes the mount-mutating/reading
    // operations so a probe's Configure/Scan/Fields cannot interleave with an
    // Assets scan and dispose/re-create the provider mid-enumeration.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Icon passes are serialised against each other so Settings' rescan and the
    // Assets screen can never run two extractors over the same cache at once.
    // (One pass at a time; a queued second pass skips everything already cached.)
    // Clearing icons also takes this gate, so a clear can never race a pass.
    private readonly SemaphoreSlim _extractGate = new(1, 1);

    // The pass currently holding _extractGate, so ClearIconsAsync can stop it
    // promptly instead of waiting for it to finish a pass over thousands of items.
    // Guarded by _passGate; null when no pass is running.
    private readonly object _passGate = new();
    private CancellationTokenSource? _activePassToken;

    private string? _knownBuildId;
    private string? _pakPath;
    private IReadOnlyList<AssetSummary> _items = Array.Empty<AssetSummary>();
    private IReadOnlyList<string> _categories = Array.Empty<string>();

    public PakContentSource() : this(new PakMount(), null, null, null) { }

    public PakContentSource(IPakMount mount) : this(mount, null, null, null) { }

    public PakContentSource(IPakMount mount, IAssetIndex? index, string? dbPath)
        : this(mount, index, dbPath, null) { }

    /// <summary>
    /// Index-backed production path: <paramref name="dbPath"/> is the workspace
    /// cache db (<c>&lt;workspace&gt;\cache\assets.db</c>). When either is null the
    /// source falls back to the in-memory list (pure unit tests only).
    /// <paramref name="icons"/> is the workspace icon cache; when omitted a cache
    /// beside the db (or in the temp folder for the in-memory path) is used.
    /// </summary>
    public PakContentSource(IPakMount mount, IAssetIndex? index, string? dbPath, IIconCache? icons)
    {
        _mount = mount;
        _index = index;
        _dbPath = dbPath;
        _icons = icons ?? new FileIconCache(DefaultIconsDirectory(dbPath));
    }

    private static string DefaultIconsDirectory(string? dbPath)
    {
        var parent = string.IsNullOrWhiteSpace(dbPath) ? null : Path.GetDirectoryName(dbPath);
        return Path.Combine(parent ?? Path.GetTempPath(), "icons");
    }

    public ContentSourceStatus Status { get; private set; } =
        new(ContentSourceState.NotConfigured, "No pak path configured.", null);

    public IReadOnlyList<string> Categories => _categories;

    public IIconCache Icons => _icons;

    public ScanInfo? LastScan
    {
        get
        {
            if (_dbPath is null || _index is null)
                return null;

            // Never block the caller (often the screen's own thread) behind a long scan: give up
            // after a moment and report "not known yet" rather than freezing.
            if (!_gate.Wait(TimeSpan.FromSeconds(2)))
                return null;

            try
            {
                return new ScanInfo(
                    _index.ReadLastScanUtc(_dbPath),
                    _index.Count(_dbPath),
                    _index.ReadBuildId(_dbPath));
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public void Configure(string pakPath, string? aesKey, string? knownBuildId)
    {
        _gate.Wait();
        try
        {
            ConfigureCore(pakPath, aesKey, knownBuildId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ConfigureCore(string pakPath, string? aesKey, string? knownBuildId)
    {
        _knownBuildId = knownBuildId;
        _pakPath = pakPath;
        _items = Array.Empty<AssetSummary>();
        _categories = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(pakPath))
        {
            Status = new(ContentSourceState.NotConfigured,
                "No SCUM pak path configured. Set it in Settings.", knownBuildId);
            return;
        }

        // A missing Content\Paks folder is its own honest state: the user's game
        // path points somewhere that has no paks to read. Plain message, no raw
        // path and no exception text (the raw details never leave the log).
        // Checked before the key so an invalid path with no key reports the path
        // problem (not a misleading "enter a key" state).
        if (!Directory.Exists(pakPath))
        {
            Status = new(ContentSourceState.Error,
                "SCUM's Content\\Paks folder wasn't found under your game path — re-detect it in Settings.",
                knownBuildId);
            return;
        }

        if (string.IsNullOrWhiteSpace(aesKey))
        {
            Status = new(ContentSourceState.NoKey,
                "An AES key is required to read the SCUM pak. Enter it in Settings.", knownBuildId);
            return;
        }

        // IPakMount.TryMount promises never to throw; a broken implementation
        // must not crash Configure. Any exception is caught here: the user
        // gets the curated plain message, the details (type + message, key
        // scrubbed) go to the log.
        bool mounted;
        string? mountError;
        try
        {
            mounted = _mount.TryMount(pakPath, aesKey, out mountError);
        }
        catch (Exception ex)
        {
            mounted = false;
            mountError = PakMount.MountFailedMessage;
            Log.Warn($"PakContentSource: mount threw {PakMount.Describe(ex, aesKey)}.");
        }

        if (!mounted)
        {
            Status = new(ContentSourceState.Error,
                mountError ?? "Failed to mount the SCUM pak.", knownBuildId);
            return;
        }

        Status = new(ContentSourceState.Ready, "SCUM pak mounted.", knownBuildId);
    }

    public async Task<ContentSourceStatus> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _gate.WaitAsync(ct);
        try
        {
            if (Status.State is ContentSourceState.NotConfigured or ContentSourceState.NoKey or ContentSourceState.Error)
                return Status;

            if (!_mount.IsMounted)
                return new ContentSourceStatus(ContentSourceState.Error,
                    "Pak is not mounted.", _knownBuildId);

            var previous = Status;
            Status = new(ContentSourceState.Scanning, "Scanning items...", _knownBuildId);

            try
            {
                // Index-backed path (production): persist to SQLite and serve searches
                // from it. A fresh (non-stale) index skips the enumeration entirely so
                // opening Assets or rescanning never re-mounts/re-walks the pak.
                if (_dbPath is not null && _index is not null)
                {
                    var buildId = ResolveBuildId();
                    if (_index.IsStale(_dbPath, buildId))
                    {
                        var items = Enumerate(progress, ct);
                        if (!ProbeItemReadable(items))
                        {
                            Status = new(ContentSourceState.Error, WrongKeyMessage, buildId);
                            return Status;
                        }

                        _index.ReplaceAll(_dbPath, buildId, items);
                        _categories = _index.Categories(_dbPath);
                        Status = new(ContentSourceState.Ready,
                            $"Indexed {items.Count} items from build {buildId}.", buildId);
                    }
                    else
                    {
                        _categories = _index.Categories(_dbPath);
                        var count = _index.Count(_dbPath);
                        Status = new(ContentSourceState.Ready,
                            $"{count} items (build {buildId}, index up to date).", buildId);
                    }

                    return Status;
                }

                // In-memory fallback (no db path): same behavior as before the index.
                var scanned = Enumerate(progress, ct);
                if (!ProbeItemReadable(scanned))
                {
                    Status = new(ContentSourceState.Error, WrongKeyMessage, _knownBuildId);
                    return Status;
                }

                _items = scanned;
                _categories = scanned
                    .Select(i => i.Category)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToArray();

                Status = new(ContentSourceState.Ready, $"Found {scanned.Count} items.", _knownBuildId);
                return Status;
            }
            catch (OperationCanceledException)
            {
                // Never leave the screen stuck on "Scanning…" when a read is stopped.
                Status = previous;
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"Scan failed unexpectedly ({ex.GetType().Name}).");
                Status = new(ContentSourceState.Error,
                    "Reading SCUM's files stopped unexpectedly. Try again.", _knownBuildId);
                return Status;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<AssetSummary>> SearchAsync(string query, string? category,
        int skip = 0, int take = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (skip < 0) skip = 0;
        if (take <= 0) return Task.FromResult<IReadOnlyList<AssetSummary>>(Array.Empty<AssetSummary>());
        if (take > MaxTake) take = MaxTake;

        // The index is the source of truth for searches (production path).
        if (_dbPath is not null && _index is not null)
            return Task.FromResult<IReadOnlyList<AssetSummary>>(
                _index.Query(_dbPath, query, category, skip, take));

        // In-memory fallback (no db path).
        var filtered = _items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(i =>
                i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.TechnicalPath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(category))
        {
            filtered = filtered.Where(i =>
                string.Equals(i.Category, category, StringComparison.Ordinal));
        }

        var page = filtered.Skip(skip).Take(take).ToArray();
        return Task.FromResult<IReadOnlyList<AssetSummary>>(page);
    }

    public async Task<IReadOnlyList<AssetField>> FieldsAsync(string assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _gate.WaitAsync(ct);
        try
        {
            if (!_mount.IsMounted || string.IsNullOrWhiteSpace(assetId))
                return Array.Empty<AssetField>();

            var fields = new List<AssetField>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Most items are blueprints: their configured values live on the class default object
            // (Default__<AssetName>_C). The bare path form would fail: CUE4Parse treats a trailing
            // ".uasset" as an object name (spike findings). A data asset (a recipe) has no default
            // object — that is not an error, so we carry on to the other reads below.
            var cdoPath = CdoPathOf(assetId);
            if (_mount.TryLoadPackageObject(cdoPath, out var cdo) && cdo is not null)
            {
                foreach (var prop in cdo.Properties)
                {
                    var technicalName = string.IsNullOrEmpty(prop.Name.Text)
                        ? prop.Name.ToString()
                        : prop.Name.Text;
                    var value = prop.Tag?.GenericValue?.ToString() ?? string.Empty;

                    // The UE property type (e.g. "IntProperty", "FloatProperty", "BoolProperty",
                    // "StrProperty", "ObjectProperty") is the honesty engine's type clue for
                    // judging write capability. Read straight off the tag so the UI never has to.
                    var typeHint = prop.PropertyType.Text;

                    // Honesty rule: nothing is known to be editable or safe yet.
                    // FriendlyName mirrors the technical name for now (Plan 3 maps it).
                    fields.Add(new AssetField(technicalName, technicalName, StatusKind.ReadOnly, value, typeHint));
                    seen.Add(technicalName);
                }
            }

            // Fields can also live on the item's component objects (e.g. MaxQuantity on a stackable
            // component); read those too, keeping one row per distinct property name.
            if (_mount.TryLoadExports(assetId, out var exports))
            {
                foreach (var export in exports)
                {
                    foreach (var prop in export.Properties)
                    {
                        var name = string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text;
                        if (!seen.Add(name)) continue;

                        var value = prop.Tag?.GenericValue?.ToString() ?? string.Empty;
                        fields.Add(new AssetField(name, name, StatusKind.ReadOnly, value, prop.PropertyType.Text));
                    }
                }
            }

            // Finally, the values hidden inside structs and lists (the well's water numbers, a
            // recipe's ingredient slots and friends). CUE4Parse hands those back as opaque slots;
            // the writer's library sees inside them. Each is addressed by a dotted path.
            if (_mount.TryReadPackage(assetId, out var rawFiles) && rawFiles is { Count: > 0 })
            {
                foreach (var nested in NestedValueReader.Read(rawFiles))
                {
                    if (seen.Add(nested.TechnicalName))
                        fields.Add(nested);
                }
            }

            return fields;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IconExtractionProgress> ExtractAllIconsAsync(
        IProgress<IconExtractionProgress>? progress = null, CancellationToken ct = default)
    {
        // Read the id list off the caller's (UI) thread too: with the index path it can
        // page thousands of rows, and the whole pass must never stall the window.
        return Task.Run(() => ExtractCore(AllAssetIds(), progress, ct), ct);
    }

    public Task<IconExtractionProgress> ExtractIconsAsync(
        IEnumerable<string> assetIds,
        IProgress<IconExtractionProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        // The whole pass runs off the caller's thread (the UI thread) so the window
        // stays responsive; the bounded internal parallelism is the extractor's.
        return Task.Run(() => ExtractCore(assetIds, progress, ct), ct);
    }

    /// <summary>
    /// Builds an extractor over the mounted provider and runs it. Producer of the
    /// "never blocks searching" guarantee: the extractor's individual package loads
    /// go through a gate-acquiring mount wrapper (see <see cref="GatedMount"/>), so
    /// the long-lived provider gate is held for one load at a time — never for the
    /// whole pass. Searching reads the SQLite index and takes no gate, so it keeps
    /// running; a Configure that re-mounts waits only for the load in flight.
    /// </summary>
    private IconExtractionProgress ExtractCore(
        IEnumerable<string> assetIds, IProgress<IconExtractionProgress>? progress, CancellationToken ct)
    {
        _extractGate.Wait(ct);

        // Links the caller's cancellation with a source-owned stop signal, so
        // ClearIconsAsync can stop this pass without cancelling the caller's token.
        CancellationTokenSource? passToken = null;
        try
        {
            if (!_mount.IsMounted)
                return new IconExtractionProgress(0, 0, 0, 0);

            passToken = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock (_passGate)
                _activePassToken = passToken;

            var iconSource = new PakIconSource(new GatedMount(_mount, _gate));
            var extractor = new ItemIconExtractor(iconSource, _icons, ResolveBuildId());
            return extractor.Extract(assetIds, progress, passToken.Token);
        }
        finally
        {
            lock (_passGate)
            {
                if (ReferenceEquals(_activePassToken, passToken))
                    _activePassToken = null;

                // Disposed under the same lock as ClearIconsAsync's Cancel, so a
                // clear can never touch a pass token that has just been disposed.
                passToken?.Dispose();
            }

            _extractGate.Release();
        }
    }

    /// <summary>
    /// Deletes every cached icon and the no-icon manifest, serialised against
    /// extraction: a running pass is cancelled first, then this takes the extraction
    /// gate so no pass can write while the cache is being emptied. A post-clear pass
    /// therefore starts from an empty cache and repopulates it cleanly.
    /// </summary>
    public async Task ClearIconsAsync(CancellationToken ct = default)
    {
        lock (_passGate)
        {
            // Cancel under the lock so the running pass cannot dispose its token
            // between our read and this call. (No token callback takes _passGate.)
            _activePassToken?.Cancel();
        }

        await _extractGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _icons.Clear();
        }
        finally
        {
            _extractGate.Release();
        }
    }

    /// <summary>
    /// Every indexed asset id, paged from the index (production) or read from the
    /// in-memory fallback. Paged because the index query caps at
    /// <see cref="ContentLimits.MaxTake"/> rows per call.
    /// </summary>
    private IReadOnlyList<string> AllAssetIds()
    {
        if (_dbPath is null || _index is null)
            return _items.Select(i => i.Id).ToArray();

        var ids = new List<string>();
        var skip = 0;
        while (true)
        {
            var batch = _index.Query(_dbPath, string.Empty, null, skip, MaxTake);
            foreach (var summary in batch)
                ids.Add(summary.Id);

            if (batch.Count < MaxTake)
                break;
            skip += MaxTake;
        }

        return ids;
    }

    /// <summary>
    /// A <see cref="IPakMount"/> view for icon extraction that takes the provider gate
    /// around each <see cref="TryLoadPackageObject"/> call (and only that call). This is
    /// what lets the long icon pass coexist with searches and with a Settings re-mount:
    /// extraction holds the gate per load, so it never freezes the other operations for
    /// the duration of the pass. Mount/enumeration pass straight through — the extractor
    /// only loads package objects, and the provider's file list is read-only.
    /// </summary>
    private sealed class GatedMount : IPakMount
    {
        private readonly IPakMount _inner;
        private readonly SemaphoreSlim _providerGate;

        public GatedMount(IPakMount inner, SemaphoreSlim providerGate)
        {
            _inner = inner;
            _providerGate = providerGate;
        }

        public bool IsMounted => _inner.IsMounted;
        public int FileCount => _inner.FileCount;
        public IEnumerable<string> FilePaths => _inner.FilePaths;

        public bool TryMount(string pakPath, string? aesKey, out string? error)
            => _inner.TryMount(pakPath, aesKey, out error);

        public bool TryLoadPackageObject(string path, out UObject? obj)
        {
            _providerGate.Wait();
            try
            {
                return _inner.TryLoadPackageObject(path, out obj);
            }
            finally
            {
                _providerGate.Release();
            }
        }

        public void Dispose()
        {
            // The wrapped mount is owned by the source, not by this view.
        }
    }

    /// <summary>
    /// The class-default-object path used to read an item's fields: the package
    /// path (with or without ".uasset") plus ".Default__&lt;AssetName&gt;_C".
    /// </summary>
    private static string CdoPathOf(string packagePath)
    {
        var path = packagePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            ? packagePath[..^7]
            : packagePath;
        var assetName = path[(path.LastIndexOf('/') + 1)..];
        return $"{path}.Default__{assetName}_C";
    }

    /// <summary>
    /// Verifies that at least one enumerated item's package can actually be
    /// decrypted, so a rotated/invalid key that still mounts and enumerates is
    /// reported as an Error rather than Ready with unreadable items. An empty
    /// enumeration has nothing to probe and is left to the caller's "no items"
    /// handling.
    /// </summary>
    /// <remarks>
    /// Samples up to <see cref="ProbeSampleSize"/> candidates spread evenly across
    /// the list rather than probing items[0] alone: the filename heuristic can
    /// produce false positives (e.g. Ammunition/12_Gauge_BirdShot on the real
    /// install) whose class/CDO does not load even with a correct key. Accepts as
    /// soon as any sampled candidate decrypts; reports a bad key only when every
    /// sampled candidate fails.
    /// </remarks>
    private bool ProbeItemReadable(IReadOnlyList<AssetSummary> items)
    {
        if (items.Count == 0)
            return true;

        var probes = Math.Min(ProbeSampleSize, items.Count);
        for (var i = 0; i < probes; i++)
        {
            // Evenly spaced index over [0, Count). When Count <= ProbeSampleSize
            // this degenerates to probing every item in order (probes == Count).
            var index = (int)((long)items.Count * i / probes);
            if (_mount.TryLoadPackageObject(CdoPathOf(items[index].Id), out _))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The build id recorded in the index: the caller-supplied known build id
    /// when present (AppSettings.BuildId), otherwise a stable marker derived
    /// from the newest file timestamp in the Paks folder.
    /// </summary>
    private string ResolveBuildId()
        => !string.IsNullOrWhiteSpace(_knownBuildId) ? _knownBuildId! : DeriveBuildId() ?? "unknown";

    // Bumped when the index's own contents change (so an old cached index rebuilds once).
    // i2: categories now come from the game's own ItemSortGroup.
    // i3: folder-only categories are aliased onto the sort-group words.
    // i4: item index only.
    // i5: base-building elements are indexed alongside items.
    private const string IndexFormat = "i5";

    private string? DeriveBuildId()
    {
        if (string.IsNullOrWhiteSpace(_pakPath) || !Directory.Exists(_pakPath))
            return null;

        try
        {
            var newest = Directory.EnumerateFiles(_pakPath)
                .Select(TryLastWriteUtc)
                .Where(t => t is not null)
                .DefaultIfEmpty((DateTime?)null)
                .Max();

            return newest is null
                ? null
                : "pak-" + newest.Value.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) + "-" + IndexFormat;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DateTime? TryLastWriteUtc(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); }
        catch (Exception) { return null; }
    }

    private List<AssetSummary> Enumerate(IProgress<int>? progress, CancellationToken ct)
    {
        var items = new List<AssetSummary>();
        // Layered paks can list the same file path more than once; index one row per id
        // (the primary key) so a duplicate can never abort the whole scan.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var total = _mount.FileCount;
        var processed = 0;
        var lastPercent = -1;

        foreach (var path in _mount.FilePaths)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            if (progress != null && total > 0)
            {
                var percent = processed * 100 / total;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(percent);
                }
            }

            if ((TryMapItem(path, out var summary) || TryMapBaseElement(path, out summary)
                 || TryMapSpawnerPreset(path, out summary))
                && seenIds.Add(summary.Id))
                items.Add(WithGameCategory(summary));
        }

        progress?.Report(100);
        return items;
    }

    /// <summary>Replaces an item's folder category with the game's own sort group when it has one.</summary>
    private AssetSummary WithGameCategory(AssetSummary summary)
    {
        if (!summary.Id.EndsWith("_ES", StringComparison.OrdinalIgnoreCase)) return summary;

        var group = SortGroupFor(summary.Id);
        return summary with { Category = group is null ? Canonical(summary.Category) : FriendlySortGroup(group) };
    }

    /// <summary>Words the game's folders use for the same kind as its sort groups (so the list has
    /// one category per kind instead of "Weapons" and "Weapon").</summary>
    private static readonly IReadOnlyDictionary<string, string> FolderAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Weapons"] = "Weapon",
            ["Ammunition"] = "Ammo",
            ["Medications"] = "Medical",
            ["Grenades"] = "Explosives",
            // Tiny folder-only categories folded into the catch-all, so the list stays short.
            ["Equipment"] = "Other",
            ["Economy"] = "Other",
            ["TwitchDrops"] = "Other",
        };

    /// <summary>The canonical category for a folder-only item (aliased onto the sort-group word).</summary>
    internal static string Canonical(string folder) =>
        FolderAliases.TryGetValue(folder, out var canonical) ? canonical : folder;

    /// <summary>Reads the game's ItemSortGroup tag ("ItemSortGroup.Food.Solid") from an item, or null.</summary>
    private string? SortGroupFor(string assetId)
    {
        if (!_mount.TryLoadPackageObject(CdoPathOf(assetId), out var cdo) || cdo is null) return null;

        foreach (var prop in cdo.Properties)
        {
            var name = string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text;
            if (!string.Equals(name, "GridInventorySortGroup", StringComparison.OrdinalIgnoreCase)) continue;

            if (prop.Tag?.GenericValue is FScriptStruct script && script.StructType is FStructFallback fb)
            {
                var tag = fb.Properties.FirstOrDefault(v =>
                    string.Equals(string.IsNullOrEmpty(v.Name.Text) ? v.Name.ToString() : v.Name.Text,
                                  "TagName", StringComparison.OrdinalIgnoreCase));
                return tag?.Tag?.GenericValue?.ToString();
            }

            return null;
        }

        return null;
    }

    /// <summary>"ItemSortGroup.Food.Solid" → "Food"; "ItemSortGroup.CraftingMaterials" → "Crafting Materials".</summary>
    internal static string FriendlySortGroup(string value)
    {
        const string prefix = "ItemSortGroup.";
        var v = value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : value;
        var head = v.Split('.')[0];
        return Regex.Replace(head, "([a-z0-9])([A-Z])", "$1 $2").Trim();
    }

    /// <summary>
    /// Maps a pak file key to an AssetSummary when it looks like an item
    /// definition, otherwise returns false. Public surface of the filename/
    /// path heuristic documented in the spike findings.
    /// </summary>
    internal static bool TryMapItem(string path, out AssetSummary summary)
    {
        summary = null!;

        if (!path.StartsWith(ItemsRoot, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = path[ItemsRoot.Length..];
        if (ExcludedSubfolders.Any(s => relative.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            return false;

        var fileName = path[(path.LastIndexOf('/') + 1)..];
        var assetName = fileName[..^7]; // strip ".uasset"
        if (ExcludedPrefixes.Any(p => assetName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        var slash = relative.IndexOf('/');
        var category = slash >= 0 ? relative[..slash] : "Uncategorized";

        summary = new AssetSummary(
            Id: path[..^7],                                  // package path (load key)
            Name: AssetNames.IsRecipeName(assetName)         // a recipe gets its plain name (no CR_)
                ? AssetNames.RecipeFriendly(assetName)
                : assetName.Replace('_', ' ').Trim(),
            Category: category,
            Overall: StatusKind.Unknown,
            EditableCount: 0,
            TechnicalPath: path,
            IconKey: null);                                  // icons are Plan 2b

        return true;
    }

    /// <summary>
    /// Maps a pak file key to an AssetSummary when it is a base-building element (a <c>BP_Base_*</c>
    /// blueprint under <see cref="BaseElementsRoot"/>), otherwise returns false. Same shape as
    /// <see cref="TryMapItem"/>, so base elements flow through the app like items.
    /// </summary>
    internal static bool TryMapBaseElement(string path, out AssetSummary summary)
    {
        summary = null!;

        if (!path.StartsWith(BaseElementsRoot, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = path[(path.LastIndexOf('/') + 1)..];
        var assetName = fileName[..^7]; // strip ".uasset"
        if (!AssetNames.IsBaseElementName(assetName))
            return false;

        summary = new AssetSummary(
            Id: path[..^7],
            Name: AssetNames.BaseElementFriendly(assetName),
            Category: "Base elements",
            Overall: StatusKind.Unknown,
            EditableCount: 0,
            TechnicalPath: path,
            IconKey: null);

        return true;
    }

    /// <summary>
    /// Maps a pak file key to an AssetSummary when it is a loot/spawn preset (a package under one
    /// of <see cref="SpawnerRoots"/>), otherwise returns false. Same shape as
    /// <see cref="TryMapItem"/>, so presets flow through the app like items. The two Items/ roots
    /// stay in <see cref="ExcludedSubfolders"/>, so a preset is never counted as an item.
    /// </summary>
    internal static bool TryMapSpawnerPreset(string path, out AssetSummary summary)
    {
        summary = null!;

        if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            return false;

        var root = SpawnerRoots.FirstOrDefault(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
        if (root is null)
            return false;

        var relative = path[root.Length..];
        var fileName = path[(path.LastIndexOf('/') + 1)..];
        var assetName = fileName[..^7]; // strip ".uasset"
        var slash = relative.IndexOf('/');
        var category = slash >= 0 ? relative[..slash] : "Uncategorized";

        summary = new AssetSummary(
            Id: path[..^7],
            Name: assetName.Replace('_', ' ').Trim(),
            Category: category,
            Overall: StatusKind.Unknown,
            EditableCount: 0,
            TechnicalPath: path,
            IconKey: null);

        return true;
    }
}
