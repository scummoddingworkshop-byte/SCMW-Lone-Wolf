using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCMW.App;

/// <summary>
/// Items' brain: the sample catalogue (clearly dummy until real SCUM data exists), the
/// search / category / "only changeable" filters, the selection, and the inline-edit
/// commands. It knows nothing about pixels.
/// </summary>
public partial class ItemsViewModel : ObservableObject
{
    private readonly List<ItemSummary> _all;
    private readonly ItemChangeStore? _changes;
    private readonly ProjectStore? _projects;
    private readonly SCMW.Core.Content.IContentSource? _source;
    private readonly ContentCatalog? _catalog;
    private ItemSummary? _editingItem;

    public ItemsViewModel(ItemChangeStore? changes = null, ProjectStore? projects = null,
                          IReadOnlyList<ItemSummary>? items = null,
                          SCMW.Core.Content.IContentSource? source = null,
                          ContentCatalog? catalog = null)
    {
        _all = (items ?? SampleItems()).ToList();
        _changes = changes;
        _projects = projects;
        _source = source;
        _catalog = catalog;

        _categories = BuildCategories();
        RebuildShownCategories();

        VisibleItems = new ObservableCollection<ItemSummary>(_all);

        if (_changes is not null) _changes.Changed += OnChangesChanged;
        if (_projects is not null) _projects.Changed += OnProjectChanged;

        if (_source is not null) SourceNote = SourceNoteFor(_source.Status.State);

        ApplyChanges();
        Refresh();
    }

    public ObservableCollection<ItemSummary> VisibleItems { get; }

    private IReadOnlyList<string> _categories;

    /// <summary>Chip names: "All" first, then the categories in the catalogue.</summary>
    public IReadOnlyList<string> Categories => _categories;

    /// <summary>How many category chips show before the "More…" button.</summary>
    public const int CategoryPreviewCount = 12;

    /// <summary>True once the player asked to see every category chip.</summary>
    [ObservableProperty] private bool _showAllCategories;

    /// <summary>
    /// The chips actually shown: the first few (plus whatever is selected) until expanded. It is a
    /// <em>stable</em> collection updated in place — replacing the ItemsSource on every notification
    /// would reset the ListBox's selection and re-enter the filter.
    /// </summary>
    public ObservableCollection<string> ShownCategories { get; } = new();

    private IReadOnlyList<string> WantedCategories()
    {
        if (ShowAllCategories || _categories.Count <= CategoryPreviewCount) return _categories;

        var shown = _categories.Take(CategoryPreviewCount).ToList();
        if (!string.IsNullOrEmpty(SelectedCategory) &&
            !shown.Contains(SelectedCategory, StringComparer.Ordinal))
        {
            shown.Add(SelectedCategory);   // never hide the one in use
        }
        return shown;
    }

    /// <summary>Brings <see cref="ShownCategories"/> in line with what should show, changing as little as possible.</summary>
    private void RebuildShownCategories()
    {
        var wanted = WantedCategories();

        for (var i = ShownCategories.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(ShownCategories[i], StringComparer.Ordinal))
                ShownCategories.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < ShownCategories.Count &&
                string.Equals(ShownCategories[i], wanted[i], StringComparison.Ordinal))
            {
                continue;
            }

            var existing = ShownCategories.IndexOf(wanted[i]);
            if (existing >= 0) ShownCategories.Move(existing, i);
            else ShownCategories.Insert(i, wanted[i]);
        }
    }

    /// <summary>True when there are more categories than the preview shows.</summary>
    public bool HasMoreCategories => _categories.Count > CategoryPreviewCount;

    /// <summary>"More…" or "Less" for the chip toggle.</summary>
    public string CategoriesToggleLabel => ShowAllCategories ? "Less" : "More…";

    [RelayCommand]
    private void ToggleCategories() => ShowAllCategories = !ShowAllCategories;

    partial void OnShowAllCategoriesChanged(bool value)
    {
        RebuildShownCategories();
        OnPropertyChanged(nameof(CategoriesToggleLabel));
    }

    private IReadOnlyList<string> BuildCategories() => new[] { "All" }
        .Concat(_all.Select(i => i.Category).Where(c => c.Length > 0)
            .Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        .ToList();

    /// <summary>True once a real SCUM item list has replaced the samples.</summary>
    public bool IsUsingRealItems { get; private set; }

    /// <summary>Plain line naming what the list currently is (empty when it is the real thing).</summary>
    [ObservableProperty] private string _sourceNote = "";

    private static string SourceNoteFor(SCMW.Core.Content.ContentSourceState state) => state switch
    {
        SCMW.Core.Content.ContentSourceState.Ready => "",
        SCMW.Core.Content.ContentSourceState.Scanning => "Reading SCUM's files…",
        SCMW.Core.Content.ContentSourceState.NoKey =>
            "Showing sample items — SCUM's files need your unlock code (Settings) to read.",
        SCMW.Core.Content.ContentSourceState.Error =>
            "Showing sample items — SCUM's files could not be read. Check Settings, then try again.",
        _ => "Showing sample items — set your SCUM folder in Settings to see the real ones.",
    };

    /// <summary>
    /// Load the real item list when the source is ready. Never throws: a failure keeps the sample
    /// list and writes a plain SourceNote.
    /// </summary>
    public async System.Threading.Tasks.Task LoadFromSourceAsync()
    {
        if (_source is null || _catalog is null) return;

        // Always ask the source to scan: it is idempotent (a fresh index is a fast no-op), and it
        // is the only thing that builds the index on a first run. Reporting Ready is not enough —
        // the real source is Ready the moment it is configured, before any scan.
        SCMW.Core.Content.ContentSourceStatus status;
        try
        {
            status = await _source.ScanAsync().ConfigureAwait(false);
        }
        catch
        {
            SourceNote = SourceNoteFor(SCMW.Core.Content.ContentSourceState.Error);
            return;
        }

        if (status.State != SCMW.Core.Content.ContentSourceState.Ready)
        {
            SourceNote = SourceNoteFor(status.State);
            return;
        }

        try
        {
            // Load the WHOLE index, not just the first page. SCUM has tens of thousands of
            // entries; a single capped page silently hid items (search only filters what is
            // loaded), which is why real items went missing. Page until the index is exhausted.
            var assets = new List<SCMW.Core.Content.AssetSummary>();
            var skip = 0;
            while (true)
            {
                var page = await _source
                    .SearchAsync("", null, skip, SCMW.Core.Content.ContentLimits.MaxTake)
                    .ConfigureAwait(false);
                if (page.Count == 0) break;
                assets.AddRange(page);
                skip += page.Count;
                if (page.Count < SCMW.Core.Content.ContentLimits.MaxTake) break;
                if (assets.Count > 200_000) break;   // safety: a source that ignores paging
            }

            if (assets.Count == 0) return;

            _all.Clear();
            _all.AddRange(_catalog.MapItems(assets));
            _categories = BuildCategories();

            // A category picked on the sample list may not exist in the real one; don't leave the
            // filter showing "No items match that" out of nowhere.
            if (!_categories.Contains(SelectedCategory, StringComparer.Ordinal))
                SelectedCategory = "All";

            IsUsingRealItems = true;
            SourceNote = "";

            OnPropertyChanged(nameof(IsUsingRealItems));
            OnPropertyChanged(nameof(Categories));
            OnPropertyChanged(nameof(HasMoreCategories));
            RebuildShownCategories();

            Refresh();

            // Icons are a fast follow: the list is already usable without them.
            await RequestIconsAsync().ConfigureAwait(false);
        }
        catch
        {
            RunOnUi(() =>
            {
                IsUsingRealItems = false;
                SourceNote = SourceNoteFor(SCMW.Core.Content.ContentSourceState.Error);
                OnPropertyChanged(nameof(IsUsingRealItems));
            });
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. The loaders await off-thread, so their
    /// continuations must not touch bound collections or properties directly.
    /// </summary>
    private static void RunOnUi(System.Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Extract icons for the visible real items and map their cached paths onto the rows.
    /// Never throws: a failure leaves letter tiles.
    /// </summary>
    public async System.Threading.Tasks.Task RequestIconsAsync()
    {
        if (_source is null || _catalog is null || !IsUsingRealItems) return;

        try
        {
            var ids = VisibleItems
                .Where(i => i.AssetId is not null)
                .Select(i => i.AssetId!)
                .ToList();
            if (ids.Count == 0) return;

            await _source.ExtractIconsAsync(ids).ConfigureAwait(false);

            RunOnUi(() =>
            {
                foreach (var item in VisibleItems.ToList())
                {
                    if (item.AssetId is { } id)
                    {
                        item.IconPath = _catalog.IconPath(id);
                    }
                }

                Refresh();
            });
        }
        catch
        {
            // A failed icon pass leaves letter tiles — never a broken image or a crash.
        }
    }

    /// <summary>Load the selected real item's fields (when it is a real item). Never throws.</summary>
    public async System.Threading.Tasks.Task LoadFieldsAsync(ItemSummary? item)
    {
        if (item is null || _source is null || _catalog is null || !IsUsingRealItems) return;

        try
        {
            // The asset id is the reader's load key (the technical path is the display description).
            var fields = await _source.FieldsAsync(item.AssetId ?? item.Description).ConfigureAwait(false);
            var values = _catalog.MapFields(fields, editableItem: item.Editable);
            RunOnUi(() =>
            {
                item.SetValues(values);
                ApplyChangesTo(item);
                OnPropertyChanged(nameof(Sections));
                OnPropertyChanged(nameof(ShowNoValues));
                OnPropertyChanged(nameof(NoValuesText));
            });
        }
        catch
        {
            // A field-load failure leaves the item with no values rather than crashing.
        }
    }

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private bool _onlyEditable = true;
    [ObservableProperty] private ItemSummary? _selectedItem;

    public bool HasItems => _all.Count > 0;
    public bool HasMatches => VisibleItems.Count > 0;
    public bool ShowEmptyState => !HasItems;
    public bool ShowNoMatches => HasItems && !HasMatches;
    public bool HasSelection => SelectedItem is not null;
    public bool ShowHint => SelectedItem is null;

    /// <summary>When true, read-only values are shown too (dimmed).</summary>
    [ObservableProperty] private bool _showAllValues;

    /// <summary>The selected item's values, grouped into sections.</summary>
    public IReadOnlyList<ValueGroup> Sections
    {
        get
        {
            var item = SelectedItem;
            if (item is null) return Array.Empty<ValueGroup>();

            return item.Values
                .Where(v => v.Editable || ShowAllValues)
                .GroupBy(v => v.Group)
                .Select(g => new ValueGroup(g.Key, g.ToList()))
                .ToList();
        }
    }

    private ItemSummary? _previousItem;

    /// <summary>True when the detail pane would be empty — so a plain line can say why.</summary>
    public bool ShowNoValues => HasSelection && Sections.Count == 0;

    /// <summary>What to say when the detail pane has nothing in it.</summary>
    public string NoValuesText => SelectedItem?.Values.Count > 0
        ? "Nothing changeable here — tick \u201cShow all values\u201d to see the rest."
        : "No values were found for this item.";

    partial void OnSelectedItemChanged(ItemSummary? value)
    {
        // Close any editor left open on the item we are leaving.
        if (_previousItem is not null)
        {
            foreach (var other in _previousItem.Values)
            {
                other.IsEditing = false;
            }
        }

        _previousItem = value;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowHint));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(ShowNoValues));
        OnPropertyChanged(nameof(NoValuesText));

        // A real item's fields are read from the game the first time it is picked.
        if (value is not null && IsUsingRealItems && value.Values.Count == 0)
        {
            _ = LoadFieldsAsync(value);
        }
    }

    partial void OnShowAllValuesChanged(bool value)
    {
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(ShowNoValues));
        OnPropertyChanged(nameof(NoValuesText));
    }

    /// <summary>Plain line shown when an edit could not be recorded (no current project).</summary>
    [ObservableProperty] private string _refusalMessage = "";

    public bool ShowRefusal => RefusalMessage.Length > 0;

    partial void OnRefusalMessageChanged(string value) => OnPropertyChanged(nameof(ShowRefusal));

    private void OnChangesChanged() => ApplyChanges();

    private void OnProjectChanged()
    {
        RefusalMessage = "";
        ApplyChanges();
    }

    /// <summary>
    /// Sets every value from the current project's records: the newest record's new value, and the
    /// first record's old value as the original. With no project (or no store) values show the game's.
    /// </summary>
    private void ApplyChanges()
    {
        var folder = _projects?.Current?.FolderPath;
        IReadOnlyList<SCMW.Core.Editing.ChangeRecord> records =
            folder is not null && _changes is not null
                ? _changes.For(folder)
                : Array.Empty<SCMW.Core.Editing.ChangeRecord>();

        foreach (var item in _all)
        {
            ApplyChangesTo(item, records);
        }

        OnPropertyChanged(nameof(Sections));
    }

    /// <summary>Applies the current project's records to one item's values (used on load and selection).</summary>
    private void ApplyChangesTo(ItemSummary item)
    {
        var folder = _projects?.Current?.FolderPath;
        IReadOnlyList<SCMW.Core.Editing.ChangeRecord> records =
            folder is not null && _changes is not null
                ? _changes.For(folder)
                : Array.Empty<SCMW.Core.Editing.ChangeRecord>();

        ApplyChangesTo(item, records);
    }

    private static void ApplyChangesTo(ItemSummary item, IReadOnlyList<SCMW.Core.Editing.ChangeRecord> records)
    {
        foreach (var value in item.Values)
        {
            var applied = SCMW.Core.Editing.AppliedValue.Resolve(records, item.Name, value.TechnicalName, value.GameValue);
            value.OriginalValue = FirstOldValue(records, item.Name, value.TechnicalName) ?? value.GameValue;
            value.Value = applied ?? value.GameValue;
        }
    }

    private static string? FirstOldValue(
        IReadOnlyList<SCMW.Core.Editing.ChangeRecord> records, string assetId, string field)
    {
        foreach (var record in records)
        {
            if (record.AssetId == assetId
                && string.Equals(record.FieldTechnicalName, field, StringComparison.OrdinalIgnoreCase))
            {
                return record.OldValue;
            }
        }

        return null;
    }

    [RelayCommand]
    private void BeginEdit(ItemValue? value)
    {
        if (value is null || !value.Editable) return;

        // Only one editor at a time.
        if (SelectedItem is { } item)
        {
            foreach (var other in item.Values)
            {
                if (!ReferenceEquals(other, value)) other.IsEditing = false;
            }
        }

        _editingItem = SelectedItem;
        value.Draft = value.Value;
        value.IsEditing = true;
    }

    [RelayCommand]
    private void CommitEdit(ItemValue? value)
    {
        if (value is null || !value.IsEditing) return;
        if (!value.IsDraftValid) return;   // stays open so it can be corrected

        if (_changes is null)
        {
            // Not wired to a project store (a bare/preview screen): the old in-memory behaviour.
            value.Value = value.Draft;
            value.IsEditing = false;
            return;
        }

        var current = _projects?.Current;
        if (current is null)
        {
            RefusalMessage = "Open or create a project first, then change its values.";
            return;   // stays open so it can be retried once a project exists
        }

        // Nothing actually changed: leave the project's records alone. Recording here would add a
        // no-op record that Build would list as a real change (and could then "build").
        if (value.Draft == value.OriginalValue)
        {
            RefusalMessage = "";
            value.Value = value.Draft;
            value.IsEditing = false;
            return;
        }

        RefusalMessage = "";
        value.Value = value.Draft;
        value.IsEditing = false;

        if (_editingItem is { } item)
        {
            _changes.RecordEdit(item.Name, value.TechnicalName, value.OriginalValue, value.Draft, current.Id);
        }
    }

    [RelayCommand]
    private void CancelEdit(ItemValue? value)
    {
        if (value is null) return;
        value.IsEditing = false;
    }

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnSelectedCategoryChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            SelectedCategory = "All";   // the chip list can clear the selection; fall back, don't filter to nothing
            return;
        }

        RebuildShownCategories();   // the selected chip must stay visible
        Refresh();
    }

    partial void OnOnlyEditableChanged(bool value) => Refresh();

    private void Refresh()
    {
        var query = Search.Trim();
        var category = string.IsNullOrEmpty(SelectedCategory) ? "All" : SelectedCategory;

        VisibleItems.Clear();
        foreach (var item in _all.Where(i =>
                     (category == "All" || i.Category == category) &&
                     (!OnlyEditable || i.Editable) &&
                     (query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            VisibleItems.Add(item);
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    [RelayCommand]
    private void SelectCategory(string? category) => SelectedCategory = category ?? "All";

    private static IReadOnlyList<ItemSummary> SampleItems() => new[]
    {
        new ItemSummary("Duct Tape", "Tools", "Sticks things together", editable: true, values: new[]
        {
            new ItemValue("Max stack", "Inventory", "10"),
            new ItemValue("Weight", "Weight", "0.5"),
            new ItemValue("Value", "Economy", "5", editable: false),
        }),
        new ItemSummary("Military Backpack", "Clothes", "Carries your loot", editable: true, values: new[]
        {
            new ItemValue("Storage slots", "Inventory", "42"),
            new ItemValue("Weight", "Weight", "3.2"),
            new ItemValue("Value", "Economy", "120", editable: false),
        }),
        new ItemSummary("AK-15", "Weapons", "A dependable rifle", editable: true, values: new[]
        {
            new ItemValue("Magazine size", "Magazine", "30"),
            new ItemValue("Damage", "Damage", "45"),
            new ItemValue("Weight", "Weight", "3.8"),
            new ItemValue("Value", "Economy", "900", editable: false),
        }),
        new ItemSummary("Canned Beans", "Food", "Tastes better than it looks", editable: true, values: new[]
        {
            new ItemValue("Calories", "Nutrition", "300"),
            new ItemValue("Water", "Nutrition", "40"),
            new ItemValue("Value", "Economy", "3", editable: false),
        }),
        new ItemSummary("Bandage", "Medical", "Stops the bleeding", editable: true, values: new[]
        {
            new ItemValue("Heal amount", "Medical", "15"),
            new ItemValue("Weight", "Weight", "0.1"),
        }),
        new ItemSummary("Rocks", "Resources", "A handful of rocks (nothing changeable yet)",
            editable: false, values: new[]
        {
            new ItemValue("Weight", "Weight", "1.0", editable: false),
        }),
    };
}
