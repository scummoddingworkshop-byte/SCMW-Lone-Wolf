// src/SCMW.App/WorkspaceViewModel.cs
// The adapting Workspace: opens one file of any kind and turns its real fields into rows,
// explained from the field library (label, unit, risk dot, plain meaning).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App;

/// <summary>Shows the real fields of whichever file is open, described by the field library.</summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly FieldLibrary _library;
    private readonly IContentSource? _source;
    private readonly ContentCatalog? _catalog;
    private readonly ItemChangeStore? _changes;
    private readonly ProjectStore? _projects;
    private bool _loadingKind;
    private ItemSummary? _suppressingSelection;

    public WorkspaceViewModel() : this(FieldLibraryLoader.LoadDefault()) { }

    public WorkspaceViewModel(FieldLibrary library, IContentSource? source = null, ContentCatalog? catalog = null,
                              ItemChangeStore? changes = null, ProjectStore? projects = null)
    {
        _library = library;
        _source = source;
        _catalog = catalog;
        _changes = changes;
        _projects = projects;

        // Choosing (or deleting) a project changes whether editing is allowed — keep the view in step.
        if (_projects is not null)
            _projects.Changed += () => OnPropertyChanged(nameof(NeedsProject));

        // An added field (e.g. the auto-destruct timer) marks its item row straight away.
        if (_changes is not null)
            _changes.Changed += OnChangesChanged;
    }

    public WorkspaceViewModel(IContentSource source) : this(FieldLibraryLoader.LoadDefault(), source) { }

    /// <summary>The rows shown, one per real field of the open file.</summary>
    public ObservableCollection<WorkspaceField> Fields { get; } = new();

    /// <summary>The opened file's name, shown as the screen's heading.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>True when the open file has at least one field to show.</summary>
    [ObservableProperty] private bool _hasFields;

    // --- Which mod you are editing (Task 4.1) ----------------------------------------------------
    // "One project = one mod": an edit is saved into the project the player picked. With none
    // picked, the Workspace refuses (and says so) rather than quietly targeting the newest project.

    /// <summary>True when a project store is wired but the player has not chosen a project yet.</summary>
    public bool NeedsProject => _changes is not null && !(_projects?.HasChosenProject ?? false);

    /// <summary>Plain words shown when <see cref="NeedsProject"/> — never jargon.</summary>
    public string ProjectWarning =>
        "Choose a project first — that's the mod your changes are saved into.";

    // --- The kind picker (Task 3.0) --------------------------------------------------------------
    // Only the kinds we have released for browsing. Loot & spawn is built, but not offered yet.

    /// <summary>The kind the Workspace opens on — the commonest thing to change.</summary>
    public const string DefaultKind = "Items";

    /// <summary>The kinds the picker offers, in the order they are shown.</summary>
    public IReadOnlyList<string> Kinds { get; } = new[] { "Items", "Base building", "Recipes" };

    /// <summary>The kind being browsed (always one of <see cref="Kinds"/>).</summary>
    [ObservableProperty] private string _selectedKind = DefaultKind;

    partial void OnSelectedKindChanged(string value)
    {
        if (!_loadingKind && !string.IsNullOrEmpty(value))
        {
            _ = LoadKindAsync(value);
        }
    }

    /// <summary>Pick a kind and load its real files. Sets <see cref="SelectedKind"/> straight away —
    /// so a caller can navigate at once — then reads off-thread. Unknown kinds fall back to Items.</summary>
    public async System.Threading.Tasks.Task OpenKindAsync(string kind)
    {
        if (string.IsNullOrEmpty(kind) || !Kinds.Contains(kind, StringComparer.Ordinal))
        {
            kind = DefaultKind;
        }

        _loadingKind = true;
        SelectedKind = kind;
        _loadingKind = false;

        await LoadKindAsync(kind).ConfigureAwait(false);
    }

    /// <summary>Which family a picker kind maps to. The library's kind names are the family names.</summary>
    private static AssetFamily FamilyOf(string kind) => kind switch
    {
        "Base building" => AssetFamily.BaseElements,
        "Recipes" => AssetFamily.Recipes,
        _ => AssetFamily.Items,
    };

    /// <summary>Read the picked kind's real files and show them. The open file is closed first, as it
    /// may belong to the kind we just left. Never throws.</summary>
    private async System.Threading.Tasks.Task LoadKindAsync(string kind)
    {
        var family = FamilyOf(kind);

        IReadOnlyList<ItemSummary> assets = Array.Empty<ItemSummary>();
        if (_source is not null && _catalog is not null)
        {
            try
            {
                var all = await AssetReader.ReadAllAsync(_source).ConfigureAwait(false);
                assets = _catalog.Map(family, all);
            }
            catch
            {
                assets = Array.Empty<ItemSummary>();   // a load failure shows an empty list, not a crash
            }
        }

        // Point each row at its icon (extracted into the reader's cache) before the list is shown.
        await AttachIconsAsync(assets).ConfigureAwait(false);

        RunOnUi(() =>
        {
            Fields.Clear();
            Title = string.Empty;
            HasFields = false;
            Browse(family.ToString(), assets);
        });
    }

    /// <summary>
    /// Extract each item's icon into the reader's cache and point its row at it, so the browse list
    /// is easy to scan. Works for every kind — an asset with no icon of its own (many base parts and
    /// recipes) simply keeps its letter tile, and an extraction failure never stops the list showing.
    /// </summary>
    private async System.Threading.Tasks.Task AttachIconsAsync(IReadOnlyList<ItemSummary> assets)
    {
        if (_source is null || _catalog is null) return;

        var ids = assets.Where(a => !string.IsNullOrEmpty(a.AssetId)).Select(a => a.AssetId!).ToList();
        if (ids.Count == 0) return;

        try
        {
            await _source.ExtractIconsAsync(ids).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        foreach (var asset in assets)
        {
            if (!string.IsNullOrEmpty(asset.AssetId))
                asset.IconPath = _catalog.IconPath(asset.AssetId);
        }
    }

    // --- Search + browse (Task 2.5) -------------------------------------------------------------
    // The browse list: the files of one kind, found by name and by the game's own category tag.

    private string _kind = string.Empty;
    private IReadOnlyList<ItemSummary> _allAssets = Array.Empty<ItemSummary>();
    private IReadOnlyList<string> _categories = new[] { "All" };

    /// <summary>The files of the browsed kind that pass the filters (search, category, editable).</summary>
    public ObservableCollection<ItemSummary> VisibleAssets { get; } = new();

    /// <summary>What the owner types to find a file by name.</summary>
    [ObservableProperty] private string _search = string.Empty;

    /// <summary>The game's own category tag in use, or "All".</summary>
    [ObservableProperty] private string _selectedCategory = "All";

    /// <summary>The file picked in the browse list, or null.</summary>
    [ObservableProperty] private ItemSummary? _selectedAsset;

    /// <summary>The category chips: "All" first, then the game's own tags present.</summary>
    public IReadOnlyList<string> Categories => _categories;

    /// <summary>True when the kind has any files to browse.</summary>
    public bool HasAssets => _allAssets.Count > 0;

    /// <summary>True when at least one file passes the filters.</summary>
    public bool HasAssetMatches => VisibleAssets.Count > 0;

    /// <summary>True when the kind has files but none pass the filters.</summary>
    public bool ShowAssetNoMatches => HasAssets && !HasAssetMatches;

    /// <summary>Load <paramref name="kind"/>'s files for browsing. The category comes straight from
    /// the game's own tag (carried on each row), so nothing is invented here.</summary>
    public void Browse(string kind, IReadOnlyList<ItemSummary> assets)
    {
        _kind = kind;
        _allAssets = assets ?? Array.Empty<ItemSummary>();
        _categories = AssetFilter.Categories(_allAssets);

        if (!_categories.Contains(SelectedCategory, StringComparer.Ordinal))
            SelectedCategory = "All";

        OnPropertyChanged(nameof(Categories));
        RefreshAssets();
    }

    private void RefreshAssets()
    {
        MarkAddedFields();
        VisibleAssets.Clear();
        foreach (var asset in AssetFilter.Apply(_allAssets, Search, SelectedCategory))
        {
            VisibleAssets.Add(asset);
        }

        OnPropertyChanged(nameof(HasAssets));
        OnPropertyChanged(nameof(HasAssetMatches));
        OnPropertyChanged(nameof(ShowAssetNoMatches));
    }

    /// <summary>Re-mark every row: does this project carry an added field for it?</summary>
    private void MarkAddedFields()
    {
        foreach (var asset in _allAssets)
            asset.AddedFieldNote = AddedNoteFor(asset);
    }

    private void OnChangesChanged() => RunOnUi(MarkAddedFields);

    /// <summary>A short note naming the field(s) this project added to the item, or empty.</summary>
    private string AddedNoteFor(ItemSummary asset)
    {
        if (string.IsNullOrEmpty(asset.AssetId)) return "";

        var records = Records();
        if (records.Count == 0) return "";

        var classPath = ClassFileOf(asset.AssetId);
        var labels = records
            .Where(r => r.Operation == ChangeOperation.AddField && IsTargetOf(r, asset.AssetId!, classPath))
            .Select(r => _library.Lookup(_kind, r.FieldTechnicalName)?.Label ?? r.FieldTechnicalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return labels.Count == 0 ? "" : string.Join(", ", labels) + " added";
    }

    partial void OnSearchChanged(string value) => RefreshAssets();

    partial void OnSelectedCategoryChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            SelectedCategory = "All";   // a chip list can clear the selection; fall back, don't filter to nothing
            return;
        }

        RefreshAssets();
    }

    [RelayCommand]
    private void SelectCategory(string? category) => SelectedCategory = category ?? "All";

    /// <summary>Open <paramref name="kind"/>'s file named <paramref name="title"/> and show the
    /// fields it actually has, each explained by the library where the library knows it.
    /// <paramref name="addable"/> lists the described fields the file lacks that we could add by
    /// borrowing a donor item's shape (Task 5.2); it is empty for a caller that has not looked.</summary>
    public void Show(string kind, string title, IReadOnlyList<AssetField> fields, string? assetPath = null,
                     IReadOnlyList<AddableField>? addable = null)
    {
        Title = title;
        Fields.Clear();

        foreach (var field in fields)
        {
            Fields.Add(Describe(kind, field));
        }

        // Show the project's recorded values for this file, addressed by its own PATH (Task 3.1).
        // The shared lookup is path-first, so two files that share a name never see each other's
        // changes; the Workspace always browses real files, so the guard keeps it path-only.
        if (!string.IsNullOrEmpty(assetPath) && Fields.Count > 0)
        {
            var records = Records();
            foreach (var field in Fields)
            {
                var applied = AppliedValue.ResolveForAsset(records, assetPath, title,
                                                           field.TechnicalName, field.GameValue);
                field.Load(applied ?? field.GameValue,
                           AppliedValue.OriginalForAsset(records, assetPath, title, field.TechnicalName)
                               ?? field.GameValue);
            }
        }

        HasFields = Fields.Count > 0;

        AddableFields.Clear();
        if (addable is not null)
        {
            foreach (var field in addable) AddableFields.Add(field);
        }
        OnPropertyChanged(nameof(HasAddableFields));
    }

    /// <summary>Open a file: remember it, then read and show ITS own fields from the game — the
    /// rows come from the file itself, never a fixed list for its kind. Never throws.</summary>
    public async System.Threading.Tasks.Task OpenAsync(ItemSummary asset)
    {
        if (asset is null) return;

        // Selecting the file is what opens it; suppress the selection hook's own load so the work
        // happens exactly once and the caller can await it.
        _suppressingSelection = asset;
        SelectedAsset = asset;
        _suppressingSelection = null;

        await LoadFieldsAsync(asset).ConfigureAwait(false);
    }

    /// <summary>Picking a file in the browse list opens it (Task 3.0).</summary>
    partial void OnSelectedAssetChanged(ItemSummary? value)
    {
        if (value is not null && !ReferenceEquals(value, _suppressingSelection))
        {
            _ = LoadFieldsAsync(value);
        }
    }

    /// <summary>Read <paramref name="asset"/>'s own fields and show them. Never throws.</summary>
    private async System.Threading.Tasks.Task LoadFieldsAsync(ItemSummary asset)
    {
        IReadOnlyList<AssetField> fields = Array.Empty<AssetField>();
        if (_source is not null)
        {
            try
            {
                fields = await _source.FieldsAsync(asset.AssetId ?? asset.Description).ConfigureAwait(false);
            }
            catch
            {
                fields = Array.Empty<AssetField>();   // a load failure shows an empty file, not a crash
            }
        }

        // Work out which described fields this file lacks and we could add (a donor item has them).
        var addable = await BuildAddableAsync(_kind, asset.AssetId, fields).ConfigureAwait(false);

        RunOnUi(() => Show(_kind, asset.Name, fields, asset.AssetId, addable));
    }

    // --- Fields you could add (Task 5.2) ----------------------------------------------------------
    // A field the file lacks is offered only when the library describes it, FLAGS it addable, AND some
    // file of the kind already has it: that file is the donor whose exact shape we copy at build time.
    // No flag, no donor, no offer — never a blank "invent a field" box. Only the auto-destruct timer
    // is flagged for now; the seam is here to widen later.

    /// <summary>How many of a kind's files we read looking for a donor before giving a field up. The
    /// auto-destruct timer's first donor sits ~90 items into the Items list, so this must clear that;
    /// the search stops at the first hit, and the answer is cached.</summary>
    private const int DonorScanLimit = 150;

    /// <summary>Per kind+field, the donor file found ("" when there is none), so we scan once.</summary>
    private readonly Dictionary<string, string> _donorCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The described fields the open file lacks that we can add by copying a donor's shape.</summary>
    public ObservableCollection<AddableField> AddableFields { get; } = new();

    /// <summary>True when at least one field could be added to the open file.</summary>
    public bool HasAddableFields => AddableFields.Count > 0;

    /// <summary>Work out the addable fields for a file: described and flagged addable, not already
    /// present, not already added, and with a donor file somewhere in the kind. Never throws.</summary>
    private async System.Threading.Tasks.Task<IReadOnlyList<AddableField>> BuildAddableAsync(
        string kind, string? assetPath, IReadOnlyList<AssetField> present)
    {
        if (_source is null || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(assetPath))
            return Array.Empty<AddableField>();

        var classPath = ClassFileOf(assetPath);

        // What the item already has, on BOTH files: the item file and its class file (the game keeps
        // item-level values like _autoDestructTime on the class file).
        var presentNames = present.Select(f => f.TechnicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (classPath is not null)
        {
            foreach (var name in await FieldNamesAsync(classPath).ConfigureAwait(false))
                presentNames.Add(name);
        }

        var addedNames = Records()
            .Where(r => r.Operation == ChangeOperation.AddField && IsTargetOf(r, assetPath, classPath))
            .Select(r => r.FieldTechnicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wanted = _library.ForKind(kind)
            .Where(d => d.Addable && !presentNames.Contains(d.Name) && !addedNames.Contains(d.Name))
            .ToList();
        if (wanted.Count == 0) return Array.Empty<AddableField>();

        var addable = new List<AddableField>();
        foreach (var definition in wanted)
        {
            var donor = await FindDonorAsync(kind, definition.Name).ConfigureAwait(false);
            if (donor is null) continue;   // no source — do not offer

            // The field lives where its donor does: on the item file, or on the item's class file.
            var targetPath = IsClassFile(donor) ? (classPath ?? assetPath) : assetPath;

            addable.Add(new AddableField(definition.Label, definition.Unit, definition.Meaning,
                                         definition.Name, donor, definition.Type, targetPath));
        }
        return addable;
    }

    /// <summary>True when a record's target is this item's own file or its class file.</summary>
    private static bool IsTargetOf(ChangeRecord record, string assetPath, string? classPath) =>
        string.Equals(record.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase)
        || (classPath is not null && string.Equals(record.AssetPath, classPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the path is a class file (an item's non-"_ES" sibling).</summary>
    private static bool IsClassFile(string path) =>
        !path.EndsWith("_ES", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The first file of the kind that already has the field, or null. An item's own class file (the
    /// name without "_ES") is checked too, since that is where the game keeps item-level values like
    /// _autoDestructTime. Scans at most <see cref="DonorScanLimit"/> files, stopping at the first hit;
    /// the answer is cached per kind+field, so a field is only ever searched for once.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> FindDonorAsync(string kind, string fieldName)
    {
        var key = kind + "\u001f" + fieldName;
        if (_donorCache.TryGetValue(key, out var cached))
            return cached.Length == 0 ? null : cached;

        string? donor = null;
        var scanned = 0;
        foreach (var asset in _allAssets)
        {
            if (scanned++ >= DonorScanLimit) break;

            foreach (var path in new[] { asset.AssetId, ClassFileOf(asset.AssetId) })
            {
                if (string.IsNullOrEmpty(path)) continue;
                var names = await FieldNamesAsync(path).ConfigureAwait(false);
                if (names.Any(n => string.Equals(n, fieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    donor = path;
                    break;
                }
            }
            if (donor is not null) break;
        }

        _donorCache[key] = donor ?? string.Empty;
        return donor;
    }

    /// <summary>The field names a file has, or none when it cannot be read. Never throws.</summary>
    private async System.Threading.Tasks.Task<IReadOnlyList<string>> FieldNamesAsync(string packagePath)
    {
        try
        {
            var fields = await _source!.FieldsAsync(packagePath).ConfigureAwait(false);
            return fields.Select(f => f.TechnicalName).ToList();
        }
        catch
        {
            return Array.Empty<string>();   // an unreadable file is simply skipped
        }
    }

    /// <summary>An item's class file: an "_ES" package path with the suffix dropped (Radio_ES ->
    /// Radio), in the same folder. Null when the name is not an "_ES" one.</summary>
    internal static string? ClassFileOf(string? packagePath)
    {
        if (string.IsNullOrEmpty(packagePath)
            || !packagePath.EndsWith("_ES", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return packagePath[..^3];
    }

    /// <summary>Add a field the file lacks: record it, borrowing the donor's shape at build time.
    /// The field is written to the file it belongs on — the item file, or the item's class file
    /// (where the game keeps item-level values like the auto-destruct timer). Refused (nothing
    /// recorded) when no project is chosen — the banner explains.</summary>
    [RelayCommand]
    private void Add(AddableField? field)
    {
        if (field is null || !field.IsDraftValid) return;

        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path) || NeedsProject) return;

        var target = string.IsNullOrEmpty(field.TargetPath) ? path! : field.TargetPath;
        _changes.RecordAddField(target, field.TechnicalName, field.Draft.Trim(), field.DonorPath,
                                _projects!.Chosen!.Id, assetPath: target);

        AddableFields.Remove(field);
        OnPropertyChanged(nameof(HasAddableFields));
    }

    /// <summary>Run an action on the UI thread (the field rows are bound to the view).</summary>
    private static void RunOnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    // --- Setting a value (Task 3.1) -------------------------------------------------------------
    // A change is recorded against the file's own PATH (never its display name), so two files that
    // share a name can never be confused.

    /// <summary>The chosen project's recorded changes (empty when no project is chosen).</summary>
    private IReadOnlyList<ChangeRecord> Records()
    {
        var folder = _projects?.Chosen?.FolderPath;
        if (folder is null || _changes is null) return Array.Empty<ChangeRecord>();
        return _changes.For(folder);
    }

    /// <summary>Open the inline editor on one field; only one editor at a time.</summary>
    [RelayCommand]
    private void BeginEdit(WorkspaceField? field)
    {
        if (field is null || !field.Writable) return;

        foreach (var other in Fields)
        {
            if (!ReferenceEquals(other, field))
            {
                other.IsEditing = false;
                other.IsMultiplying = false;
            }
        }

        field.Draft = field.Value;
        field.IsEditing = true;
    }

    /// <summary>Apply a typed value: record it against the file's PATH (never its name).</summary>
    [RelayCommand]
    private void CommitEdit(WorkspaceField? field)
    {
        if (field is null || !field.IsEditing) return;
        if (!field.IsDraftValid) return;   // stays open so it can be corrected

        var draft = field.Draft.Trim();
        var path = SelectedAsset?.AssetId;

        // Not wired to a project (a bare/preview screen): keep the change in memory only.
        if (_changes is null || string.IsNullOrEmpty(path))
        {
            field.Value = draft;
            field.IsEditing = false;
            return;
        }

        // No project chosen yet: refuse — never quietly save into the newest project.
        if (NeedsProject)
        {
            field.IsEditing = false;   // leave the shown value alone; the banner says why
            return;
        }

        // Nothing actually changed: leave the records alone (a no-op record would be listed by
        // Build as a real change).
        if (draft == field.OriginalValue)
        {
            field.Value = draft;
            field.IsEditing = false;
            return;
        }

        field.Value = draft;
        field.IsEditing = false;
        _changes.RecordEdit(path!, field.TechnicalName, field.OriginalValue, draft, _projects!.Chosen!.Id,
                            assetPath: path);
    }

    [RelayCommand]
    private void CancelEdit(WorkspaceField? field)
    {
        if (field is null) return;
        field.IsEditing = false;
    }

    /// <summary>Open the factor editor on one field ("×2", "half", "+50%"); only one editor at a time.</summary>
    [RelayCommand]
    private void BeginMultiply(WorkspaceField? field)
    {
        if (field is null || !field.CanMultiply) return;

        foreach (var other in Fields)
        {
            if (!ReferenceEquals(other, field))
            {
                other.IsEditing = false;
                other.IsMultiplying = false;
            }
        }

        field.FactorDraft = string.Empty;
        field.IsMultiplying = true;
    }

    /// <summary>Apply a factor: record a multiply against the file's PATH (never its name). The factor
    /// scales the game's own value, so the newest change to the field wins.</summary>
    [RelayCommand]
    private void CommitMultiply(WorkspaceField? field)
    {
        if (field is null || !field.IsMultiplying) return;
        if (!field.IsFactorValid) return;   // stays open so it can be corrected

        Multiplier.TryParse(field.FactorDraft, out var factor);
        var product = Multiplier.Apply(field.GameValue, factor);
        var path = SelectedAsset?.AssetId;

        // No project chosen yet: refuse — never quietly save into the newest project.
        if (NeedsProject)
        {
            field.IsMultiplying = false;   // leave the shown value alone; the banner says why
            return;
        }

        field.IsMultiplying = false;
        field.Load(product ?? field.GameValue, field.GameValue);

        // Not wired to a project (a bare/preview screen): keep the change in memory only.
        if (_changes is null || string.IsNullOrEmpty(path)) return;

        _changes.RecordMultiply(path!, field.TechnicalName, field.GameValue,
                                factor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                _projects!.Chosen!.Id, assetPath: path);
    }

    [RelayCommand]
    private void CancelMultiply(WorkspaceField? field)
    {
        if (field is null) return;
        field.IsMultiplying = false;
    }

    /// <summary>Describe one real field through the library. When the library does not know it,
    /// fall back to the engine's own friendly label and an honest undescribed state — a field is
    /// never hidden or guessed.</summary>
    private WorkspaceField Describe(string kind, AssetField field)
    {
        var definition = _library.Lookup(kind, field.TechnicalName);
        return new WorkspaceField(
            label: definition?.Label ?? FieldNames.Friendly(field.TechnicalName),
            unit: definition?.Unit ?? string.Empty,
            value: FieldKnowledge.FormatValue(field.CurrentValue),
            risk: definition?.Risk ?? Risk.Unknown,
            writable: definition?.Writable ?? false,
            meaning: definition?.Meaning ?? string.Empty,
            technicalName: field.TechnicalName,
            described: definition is not null,
            type: definition?.Type ?? FieldType.Unknown);
    }
}

/// <summary>One row of the Workspace: a real field, described for the owner, and — when we can write
/// it — editable by typing a value (Task 3.1).</summary>
public partial class WorkspaceField : ObservableObject
{
    public WorkspaceField(string label, string unit, string value, Risk risk,
                          bool writable, string meaning, string technicalName, bool described,
                          FieldType type = FieldType.Unknown)
    {
        Label = label;
        Unit = unit;
        _value = value;
        _originalValue = value;
        GameValue = value;
        Risk = risk;
        Writable = writable;
        Meaning = meaning;
        TechnicalName = technicalName;
        Described = described;
        Type = type;
    }

    /// <summary>What the owner reads.</summary>
    public string Label { get; }

    /// <summary>The unit shown beside the label (empty for a plain count).</summary>
    public string Unit { get; }

    public bool HasUnit => !string.IsNullOrWhiteSpace(Unit);

    /// <summary>The kind of value, from the library (Unknown when the field is not described).</summary>
    public FieldType Type { get; }

    /// <summary>The value the game itself has, before any project record is applied.</summary>
    public string GameValue { get; }

    /// <summary>The field's current value, shown for reading.</summary>
    [ObservableProperty] private string _value;

    /// <summary>The value before the project's first change (the "was …" baseline).</summary>
    [ObservableProperty] private string _originalValue;

    /// <summary>How risky a wrong value is — the colour of the row's dot.</summary>
    public Risk Risk { get; }

    /// <summary>True when the writer can set this field.</summary>
    public bool Writable { get; }

    /// <summary>True when the value can be typed into (the same as <see cref="Writable"/>).</summary>
    public bool Editable => Writable;

    /// <summary>A one-line plain meaning, or empty when not described.</summary>
    public string Meaning { get; }

    public bool HasMeaning => !string.IsNullOrWhiteSpace(Meaning);

    /// <summary>The game's own field name (used to address a change, never the display name).</summary>
    public string TechnicalName { get; }

    /// <summary>True when the field library describes this field.</summary>
    public bool Described { get; }

    /// <summary>True when the shown value differs from the baseline (we changed it).</summary>
    public bool IsChanged => Value != OriginalValue;

    /// <summary>Plain "was 10" line shown under a changed value.</summary>
    public string WasText => $"was {OriginalValue}";

    /// <summary>True while the inline editor is open.</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>True when the clickable value should show (writable and not being edited).</summary>
    public bool ShowValueButton => Writable && !IsEditing && !IsMultiplying;

    /// <summary>True when the field is a number we can multiply (multiplying text makes no sense).</summary>
    public bool CanMultiply => Writable && Type == FieldType.Number;

    /// <summary>True while the factor editor is open.</summary>
    [ObservableProperty] private bool _isMultiplying;

    /// <summary>What the owner types as the factor ("2", "half", "+50%").</summary>
    [ObservableProperty] private string _factorDraft = "";

    /// <summary>Whether the typed text is a factor we can use.</summary>
    public bool IsFactorValid => Multiplier.TryParse(FactorDraft, out _);

    /// <summary>True when the × button should show (a number we can multiply, nothing else open).</summary>
    public bool ShowMultiplyButton => CanMultiply && !IsEditing && !IsMultiplying;

    /// <summary>What the owner is typing; only committed when it is sane for the field's kind.</summary>
    [ObservableProperty] private string _draft = "";

    /// <summary>Whether the draft is a plausible value: a number for a number, Yes/No for a switch,
    /// and any non-blank text otherwise.</summary>
    public bool IsDraftValid => Type switch
    {
        // Numbers use the game's format (dot decimal) whatever the machine's culture.
        FieldType.Number => double.TryParse(Draft, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _),
        FieldType.Boolean => IsYesNo(Draft),
        _ => !string.IsNullOrWhiteSpace(Draft),
    };

    /// <summary>True for a switch value as shown ("Yes"/"No") or as the game writes it (true/false, 1/0).</summary>
    internal static bool IsYesNo(string? draft)
    {
        var text = (draft ?? string.Empty).Trim();
        return bool.TryParse(text, out _)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("no", StringComparison.OrdinalIgnoreCase)
            || text == "1" || text == "0";
    }

    /// <summary>Set the shown value and the "was …" baseline (used when records are applied).</summary>
    public void Load(string value, string original)
    {
        Value = value;
        OriginalValue = original;
    }

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(IsChanged));

    partial void OnOriginalValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(WasText));
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowValueButton));
        OnPropertyChanged(nameof(ShowMultiplyButton));
    }

    partial void OnIsMultiplyingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowValueButton));
        OnPropertyChanged(nameof(ShowMultiplyButton));
    }

    partial void OnFactorDraftChanged(string value) => OnPropertyChanged(nameof(IsFactorValid));

    partial void OnDraftChanged(string value) => OnPropertyChanged(nameof(IsDraftValid));

    /// <summary>The honest line shown for a field we have not worked out yet.</summary>
    public const string NotDescribed = "We haven't worked out what this does yet.";

    /// <summary>The line under the field: its plain meaning, or the honest note that we have not
    /// worked it out yet. An undescribed field is never hidden.</summary>
    public string Detail => Described ? Meaning : NotDescribed;

    /// <summary>True when there is a line to show under the field.</summary>
    public bool HasDetail => Described ? HasMeaning : true;

    public bool IsLow => Risk == Risk.Low;

    public bool IsMedium => Risk == Risk.Medium;

    public bool IsHigh => Risk == Risk.High;

    public bool IsUnknown => Risk == Risk.Unknown;
}

/// <summary>
/// One entry in the Workspace's "Fields you could add" list: a described field the open file does
/// not have, together with the donor file whose exact shape is copied when it is added (Task 5.2).
/// </summary>
public partial class AddableField : ObservableObject
{
    public AddableField(string label, string unit, string meaning, string technicalName,
                        string donorPath, FieldType type, string targetPath)
    {
        Label = label;
        Unit = unit;
        Meaning = meaning;
        TechnicalName = technicalName;
        DonorPath = donorPath;
        Type = type;
        TargetPath = targetPath;
    }

    /// <summary>What the owner reads.</summary>
    public string Label { get; }

    /// <summary>The unit shown beside the label (empty for a plain count).</summary>
    public string Unit { get; }

    public bool HasUnit => !string.IsNullOrWhiteSpace(Unit);

    /// <summary>A one-line plain meaning.</summary>
    public string Meaning { get; }

    public bool HasMeaning => !string.IsNullOrWhiteSpace(Meaning);

    /// <summary>The game's own field name (used to address the change, never the display name).</summary>
    public string TechnicalName { get; }

    /// <summary>The game file that already has the field — where the shape is borrowed from.</summary>
    public string DonorPath { get; }

    /// <summary>The file the field is written to: the item file, or its class file.</summary>
    public string TargetPath { get; }

    /// <summary>The kind of value, from the library.</summary>
    public FieldType Type { get; }

    /// <summary>What the owner types as the new field's value.</summary>
    [ObservableProperty] private string _draft = "";

    /// <summary>Whether the draft is a plausible value for the field's kind.</summary>
    public bool IsDraftValid => Type switch
    {
        FieldType.Number => double.TryParse(Draft, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _),
        FieldType.Boolean => WorkspaceField.IsYesNo(Draft),
        _ => !string.IsNullOrWhiteSpace(Draft),
    };

    partial void OnDraftChanged(string value) => OnPropertyChanged(nameof(IsDraftValid));
}
