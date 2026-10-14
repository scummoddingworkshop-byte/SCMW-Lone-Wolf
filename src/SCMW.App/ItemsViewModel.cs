using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

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
    private readonly AssetFamily _family;
    private ItemSummary? _editingItem;

    /// <summary>True when this screen shows base-building elements rather than items.</summary>
    private bool IsBase => _family == AssetFamily.BaseElements;

    /// <summary>True when this screen shows crafting recipes.</summary>
    private bool IsRecipes => _family == AssetFamily.Recipes;

    /// <summary>The real crafting-tag names the game has (loaded when a recipe is opened), so a link
    /// edit can be checked instead of silently writing a tag the game doesn't know.</summary>
    private IReadOnlySet<string> _linkChoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>True for an ingredient's accepted-tag link (the only retargetable link).</summary>
    private static bool IsRetargetableLink(ItemValue value) =>
        value.Band == ValueBand.Noted
        && value.TechnicalName.Contains("AllowedTypes[", StringComparison.OrdinalIgnoreCase);

    /// <summary>Watermark for the search box.</summary>
    public string SearchWatermark => IsBase ? "Search base parts…" : IsRecipes ? "Search recipes…" : "Search items…";

    /// <summary>The "only _ES items" toggle applies to items only.</summary>
    public bool ShowOnlyEditableToggle => !IsBase && !IsRecipes;

    /// <summary>Plain line when nothing matches the current filter.</summary>
    public string NoMatchesText => IsBase ? "No base parts match that" : IsRecipes ? "No recipes match that" : "No items match that";

    /// <summary>Plain heading when there is nothing to list.</summary>
    public string EmptyTitle => IsBase ? "No base parts yet" : IsRecipes ? "No recipes yet" : "No items yet";

    /// <summary>Plain line under the empty heading.</summary>
    public string EmptyText => "They'll appear here once SCUM's files are read.";

    /// <summary>Plain heading in the detail pane before anything is picked.</summary>
    public string HintTitle => IsBase
        ? "Pick a base part to see what you can change"
        : IsRecipes
            ? "Pick a recipe to see what you can change"
            : "Pick an item to see what you can change";

    /// <summary>Plain line under the hint heading.</summary>
    public string HintText => "Choose something from the list on the left.";

    public ItemsViewModel(ItemChangeStore? changes = null, ProjectStore? projects = null,
                          IReadOnlyList<ItemSummary>? items = null,
                          SCMW.Core.Content.IContentSource? source = null,
                          ContentCatalog? catalog = null,
                          AssetFamily family = AssetFamily.Items)
    {
        _all = (items ?? (family == AssetFamily.Items ? SampleItems() : Array.Empty<ItemSummary>())).ToList();
        _changes = changes;
        _projects = projects;
        _source = source;
        _catalog = catalog;
        _family = family;

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

    private IReadOnlyList<string> BuildCategories() => AssetFilter.Categories(_all);

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
            RunOnUi(() => SourceNote = SourceNoteFor(status.State));
            return;
        }

        try
        {
            // Load the WHOLE index, not just the first page — AssetReader pages to exhaustion.
            // A single capped page silently hid items (search only filters what is loaded).
            var assets = await AssetReader.ReadAllAsync(_source).ConfigureAwait(false);

            if (assets.Count == 0) return;

            // Everything that touches what is on screen happens on the screen's own thread.
            RunOnUi(() =>
            {
                _all.Clear();
                _all.AddRange(_catalog.Map(_family, assets));
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
            });

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
        if (_family != AssetFamily.Items) return;   // base parts and recipes show letter tiles for now
        if (_source is null || _catalog is null || !IsUsingRealItems) return;

        try
        {
            // Read the on-screen list on its own thread, then do the slow work off it.
            List<string> ids = new();
            RunOnUi(() => ids = VisibleItems
                .Where(i => i.AssetId is not null)
                .Select(i => i.AssetId!)
                .ToList());
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

            // A recipe's accepted tags are links; learn the real tag names so a typed one can be checked.
            if (values.Any(IsRetargetableLink))
                _linkChoices = await LoadLinkChoicesAsync().ConfigureAwait(false);

            RunOnUi(() =>
            {
                item.SetValues(values);
                ApplyChangesTo(item);
                OnPropertyChanged(nameof(Sections));
                OnPropertyChanged(nameof(CanManageIngredients));
                OnPropertyChanged(nameof(ShowNoValues));
                OnPropertyChanged(nameof(NoValuesText));
            });
        }
        catch
        {
            // A field-load failure leaves the item with no values rather than crashing.
        }
    }

    /// <summary>The real crafting-tag names (CI_*) from the game, for checking a link edit.</summary>
    private async System.Threading.Tasks.Task<IReadOnlySet<string>> LoadLinkChoicesAsync()
    {
        if (_source is null) return _linkChoices;

        try
        {
            var hits = await _source
                .SearchAsync("CI_", null, 0, SCMW.Core.Content.ContentLimits.MaxTake)
                .ConfigureAwait(false);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hit in hits)
            {
                var slash = hit.Id.LastIndexOf('/');
                var name = slash >= 0 ? hit.Id[(slash + 1)..] : hit.Id;
                if (name.StartsWith("CI_", StringComparison.OrdinalIgnoreCase)) names.Add(name);
            }
            return names;
        }
        catch
        {
            return _linkChoices;   // couldn't check: keep whatever we had
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

            return GroupSections(item.Values.Where(v => v.Editable || ShowAllValues));
        }
    }

    /// <summary>The fixed order of sections, whatever order the game lists a file's fields in.</summary>
    private static readonly string[] SectionOrder =
    {
        "Inventory", "Gameplay", "Appearance", "ItemSettings", "Other",
    };

    /// <summary>
    /// Groups values into sections in a fixed order, and sorts the values inside each section by
    /// their shown name — so every item reads the same way, no matter how its file is ordered.
    /// </summary>
    internal static IReadOnlyList<ValueGroup> GroupSections(IEnumerable<ItemValue> values)
    {
        return values
            .GroupBy(v => v.Group)
            .OrderBy(g => SectionRank(g.Key))
            .ThenBy(g => SectionSortKey(g.Key), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ordered = g.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var slot = SlotOf(g.First().TechnicalName);
                return new ValueGroup(g.Key, ordered,
                    slot is null ? null : SlotPrefix + slot.Value + "]");
            })
            .ToList();
    }

    /// <summary>A section's rank in the fixed order; an unrecognised section sorts last.</summary>
    private static int SectionRank(string name)
    {
        var index = Array.FindIndex(SectionOrder,
            s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? SectionOrder.Length : index;
    }

    /// <summary>A tie-breaker so "Ingredient 2" sorts before "Ingredient 10".</summary>
    private static string SectionSortKey(string name)
    {
        const string ingredient = "Ingredient ";
        if (name.StartsWith(ingredient, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[ingredient.Length..], out var number))
        {
            return ingredient + number.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        }
        return name;
    }

    /// <summary>True when the selected item has ingredient slots, so Add/Remove can be offered.</summary>
    public bool CanManageIngredients =>
        SelectedItem is { } item && item.GameValues.Any(v => SlotOf(v.TechnicalName) is not null);

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
        OnPropertyChanged(nameof(CanManageIngredients));
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
        OnPropertyChanged(nameof(CanManageIngredients));
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

    private static void ApplyChangesTo(ItemSummary item, IReadOnlyList<ChangeRecord> records)
    {
        // List changes (add/remove an ingredient slot) first, then the value edits on top — the
        // same order the build applies them, so what is shown matches what is built.
        item.ReplaceValues(ApplyListOps(item.GameValues, records, item.AssetId, item.Name));

        foreach (var value in item.Values)
        {
            var applied = AppliedValue.ResolveForAsset(records, item.AssetId, item.Name,
                                                       value.TechnicalName, value.GameValue);
            value.OriginalValue = AppliedValue.OriginalForAsset(records, item.AssetId, item.Name,
                                                               value.TechnicalName) ?? value.GameValue;
            value.Value = applied ?? value.GameValue;
        }
    }

    /// <summary>The ingredient-slot field prefix (a recipe's list of ingredients).</summary>
    private const string SlotPrefix = "Ingredients[";

    /// <summary>Applies the recorded add/remove list changes to the game's values, in order.</summary>
    private static IReadOnlyList<ItemValue> ApplyListOps(
        IReadOnlyList<ItemValue> gameValues, IReadOnlyList<ChangeRecord> records,
        string? assetPath, string assetName)
    {
        var ops = records
            .Where(r => r.Operation != ChangeOperation.Set && MatchesAsset(r, assetPath, assetName))
            .ToList();
        if (ops.Count == 0) return gameValues;

        var values = gameValues.ToList();
        foreach (var op in ops)
        {
            if (op.Operation == ChangeOperation.AddEntry)
                values = AddSlot(values);
            else if (SlotOf(op.FieldTechnicalName) is { } index)
                values = RemoveSlot(values, index);
        }
        return values;
    }

    /// <summary>Adds a slot by copying the last one's values with the next index.</summary>
    private static List<ItemValue> AddSlot(List<ItemValue> values)
    {
        var highest = values
            .Select(v => SlotOf(v.TechnicalName))
            .Where(i => i is not null)
            .Select(i => i!.Value)
            .DefaultIfEmpty(-1)
            .Max();
        if (highest < 0) return values;

        var next = highest + 1;
        var result = new List<ItemValue>(values);
        foreach (var value in values.Where(v => SlotOf(v.TechnicalName) == highest))
            result.Add(Rewrite(value, ReplaceSlot(value.TechnicalName, next), useGameValue: true));
        return result;
    }

    /// <summary>Removes a slot and shifts the later slots down so their paths stay right.</summary>
    private static List<ItemValue> RemoveSlot(List<ItemValue> values, int index)
    {
        var result = new List<ItemValue>();
        foreach (var value in values)
        {
            var slot = SlotOf(value.TechnicalName);
            if (slot == index) continue;

            result.Add(slot is not null && slot > index
                ? Rewrite(value, ReplaceSlot(value.TechnicalName, slot.Value - 1))
                : value);
        }
        return result;
    }

    /// <summary>
    /// A copy of a value under a new path, with its section heading kept in step. A slot added by
    /// copying the last one uses the last one's <em>game</em> value, matching what the build writes.
    /// </summary>
    private static ItemValue Rewrite(ItemValue value, string newPath, bool useGameValue = false)
    {
        var slot = SlotOf(newPath);
        var group = slot is null ? value.Group : "Ingredient " + (slot.Value + 1);
        var shown = useGameValue ? value.GameValue : value.Value;
        return new ItemValue(value.Name, group, shown, value.Kind, value.Editable,
            newPath, value.Band, value.Note);
    }

    /// <summary>The ingredient slot a path belongs to ("Ingredients[2]…" → 2), or null.</summary>
    private static int? SlotOf(string path)
    {
        if (!path.StartsWith(SlotPrefix, StringComparison.Ordinal)) return null;
        var close = path.IndexOf(']', SlotPrefix.Length);
        return close > 0 && int.TryParse(path[SlotPrefix.Length..close], out var index) ? index : null;
    }

    /// <summary>Replaces a path's slot index with <paramref name="index"/>.</summary>
    private static string ReplaceSlot(string path, int index)
    {
        var close = path.IndexOf(']', SlotPrefix.Length);
        return SlotPrefix + index + "]" + path[(close + 1)..];
    }

    [RelayCommand]
    private void AddIngredient()
    {
        var current = _projects?.Current;
        if (_changes is null || SelectedItem is not { } item) return;
        if (current is null) { RefusalMessage = "Open or create a project first, then change its values."; return; }

        RefusalMessage = "";
        _changes.RecordListChange(item.Name, "Ingredients", ChangeOperation.AddEntry, current.Id,
            assetPath: item.AssetId);
    }

    [RelayCommand]
    private void RemoveIngredient(string? slotPath)
    {
        var current = _projects?.Current;
        if (_changes is null || SelectedItem is not { } item || string.IsNullOrEmpty(slotPath)) return;
        if (current is null) { RefusalMessage = "Open or create a project first, then change its values."; return; }

        var dropped = _changes.RecordListChange(item.Name, slotPath, ChangeOperation.RemoveEntry, current.Id,
            assetPath: item.AssetId);

        // Say so plainly: removing a slot shifts the others, so the recipe's value edits were reset.
        RefusalMessage = dropped > 0
            ? "Ingredient removed. Your earlier value edits to this recipe were reset — please re-do them."
            : "";
    }

    /// <summary>Whether a record belongs to the asset: by the file's path when it has one, else by name.</summary>
    private static bool MatchesAsset(ChangeRecord record, string? assetPath, string assetName)
        => string.IsNullOrEmpty(assetPath)
            ? string.Equals(record.AssetId, assetName, StringComparison.OrdinalIgnoreCase)
            : string.Equals(record.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase);

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

        // A link must name a real game tag — refuse an invented one rather than write a dead link.
        if (IsRetargetableLink(value) && _linkChoices.Count > 0 && !_linkChoices.Contains(value.Draft.Trim()))
        {
            RefusalMessage = "That isn't a crafting tag SCUM has — choose a real one (they start with CI_).";
            return;   // stays open so it can be corrected
        }

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
            if (!_changes.RecordEdit(item.Name, value.TechnicalName, value.OriginalValue, value.Draft, current.Id,
                    assetPath: item.AssetId))
                RefusalMessage = "That change could not be saved — check the project folder, then try again.";
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
        VisibleItems.Clear();
        foreach (var item in AssetFilter.Apply(_all, Search, SelectedCategory, OnlyEditable))
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
