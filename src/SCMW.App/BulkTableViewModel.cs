// src/SCMW.App/BulkTableViewModel.cs
// The bulk table (Task 5.1): pick many files — or all the ones a category shows — choose one
// field, type one value, and each chosen file gets its own recorded change. Changes are always
// addressed by the file's own PATH, never its display name.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Editing;
using SCMW.Core.Knowledge;

namespace SCMW.App;

/// <summary>Shows a kind's files as a tick-list and sets one field's value across the ticked ones.</summary>
public partial class BulkTableViewModel : ObservableObject
{
    private readonly FieldLibrary _library;
    private readonly IContentSource? _source;
    private readonly ContentCatalog? _catalog;
    private readonly ItemChangeStore? _changes;
    private readonly ProjectStore? _projects;
    private bool _loadingKind;

    public BulkTableViewModel() : this(FieldLibraryLoader.LoadDefault()) { }

    public BulkTableViewModel(FieldLibrary library, IContentSource? source = null, ContentCatalog? catalog = null,
                              ItemChangeStore? changes = null, ProjectStore? projects = null)
    {
        _library = library;
        _source = source;
        _catalog = catalog;
        _changes = changes;
        _projects = projects;

        // Choosing (or deleting) a project changes whether the bulk set is allowed — keep the view in step.
        if (_projects is not null)
            _projects.Changed += () => OnPropertyChanged(nameof(NeedsProject));
    }

    /// <summary>The tick-list: one row per file the filters show.</summary>
    public ObservableCollection<BulkRow> Rows { get; } = new();

    // --- Which mod you are editing (Task 4.1: "one project = one mod") --------------------------

    /// <summary>True when a project store is wired but the player has not chosen a project yet.</summary>
    public bool NeedsProject => _changes is not null && !(_projects?.HasChosenProject ?? false);

    /// <summary>Plain words shown when <see cref="NeedsProject"/> — never jargon.</summary>
    public string ProjectWarning => "Choose a project first — that's the mod your changes are saved into.";

    // --- The kind picker (mirrors the Workspace) ------------------------------------------------

    /// <summary>The kind the table opens on — the commonest thing to change.</summary>
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

    /// <summary>Pick a kind and load its real files. Sets <see cref="SelectedKind"/> straight away
    /// then reads off-thread. Unknown kinds fall back to Items.</summary>
    public async Task OpenKindAsync(string kind)
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

    /// <summary>Read the picked kind's real files and show them. Never throws.</summary>
    private async Task LoadKindAsync(string kind)
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

        var familyName = family.ToString();
        RunOnUi(() => Load(familyName, assets));
    }

    // --- Search + browse (reuses the shared AssetFilter) ----------------------------------------

    private string _kind = string.Empty;
    private IReadOnlyList<ItemSummary> _allAssets = Array.Empty<ItemSummary>();

    /// <summary>The game's own category tags present, "All" first.</summary>
    [ObservableProperty] private IReadOnlyList<string> _categories = new[] { "All" };

    /// <summary>What the owner types to find a file by name.</summary>
    [ObservableProperty] private string _search = string.Empty;

    /// <summary>The game's own category tag in use, or "All".</summary>
    [ObservableProperty] private string _selectedCategory = "All";

    /// <summary>When ticked, only files the writer can change are listed.</summary>
    [ObservableProperty] private bool _onlyEditable = true;

    /// <summary>True when the kind has any files to show.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Load <paramref name="kind"/>'s files (kind is the field-library name, e.g. "Items").</summary>
    public void Load(string kind, IReadOnlyList<ItemSummary> assets)
    {
        _kind = kind;
        _allAssets = assets ?? Array.Empty<ItemSummary>();
        Categories = AssetFilter.Categories(_allAssets);

        if (!Categories.Contains(SelectedCategory, StringComparer.Ordinal))
            SelectedCategory = "All";

        Fields = _library.ForKind(kind).Where(f => f.Writable).ToList();
        RefreshRows();
    }

    private void RefreshRows()
    {
        Rows.Clear();
        foreach (var asset in AssetFilter.Apply(_allAssets, Search, SelectedCategory, OnlyEditable))
        {
            var row = new BulkRow(asset);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        NotifySelection();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkRow.IsSelected)) NotifySelection();
    }

    partial void OnSearchChanged(string value) => RefreshRows();

    partial void OnOnlyEditableChanged(bool value) => RefreshRows();

    partial void OnSelectedCategoryChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            SelectedCategory = "All";   // a chip list can clear the selection; fall back, don't filter to nothing
            return;
        }

        RefreshRows();
    }

    [RelayCommand]
    private void SelectCategory(string? category) => SelectedCategory = category ?? "All";

    // --- Selecting many (Task 5.1) --------------------------------------------------------------

    /// <summary>How many files are ticked.</summary>
    public int SelectedCount => Rows.Count(r => r.IsSelected);

    /// <summary>True when at least one file is ticked.</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>Tick every file the filters show (so "all Food" is one tick).</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows) row.IsSelected = true;
        NotifySelection();
    }

    /// <summary>Untick every file.</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
        NotifySelection();
    }

    // --- Setting one value on the ticked files --------------------------------------------------

    /// <summary>The fields we can set, from the library only (never an invented one).</summary>
    [ObservableProperty] private IReadOnlyList<FieldDefinition> _fields = Array.Empty<FieldDefinition>();

    /// <summary>The field being set across the ticked files.</summary>
    [ObservableProperty] private FieldDefinition? _selectedField;

    /// <summary>What the owner types as the new value.</summary>
    [ObservableProperty] private string _value = string.Empty;

    /// <summary>Whether the typed value is sane for the chosen field's kind.</summary>
    public bool IsValueValid => SelectedField switch
    {
        null => false,
        // Numbers use the game's format (dot decimal) whatever the machine's culture.
        { Type: FieldType.Number } => double.TryParse(Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _),
        { Type: FieldType.Boolean } => IsYesNo(Value),
        _ => !string.IsNullOrWhiteSpace(Value),
    };

    /// <summary>True for a switch value as shown ("Yes"/"No") or as the game writes it (true/false, 1/0).</summary>
    private static bool IsYesNo(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return bool.TryParse(text, out _)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("no", StringComparison.OrdinalIgnoreCase)
            || text == "1" || text == "0";
    }

    /// <summary>True when there is something to apply: files ticked, a field, a good value, a project.</summary>
    public bool CanApply => HasSelection && SelectedField is not null && IsValueValid && !NeedsProject;

    /// <summary>What happened last, in plain words (empty when nothing has been tried).</summary>
    [ObservableProperty] private string _resultText = string.Empty;

    /// <summary>True when there is a result line to show.</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    /// <summary>
    /// Set the chosen value on every ticked file — one recorded change per file, addressed by its own
    /// path, so two files that share a name can never be confused. Never throws; says plainly what
    /// happened (or why nothing did).
    /// </summary>
    [RelayCommand]
    private async Task Apply() => await ApplyAsync();

    /// <summary>The body of <see cref="Apply"/> (public so it can be awaited directly).</summary>
    public async Task ApplyAsync()
    {
        ResultText = string.Empty;

        if (!HasSelection)
        {
            ResultText = "Tick the files you want to change first.";
            return;
        }

        if (SelectedField is null)
        {
            ResultText = "Choose which value to set.";
            return;
        }

        if (!IsValueValid)
        {
            ResultText = "That value doesn't fit this one — check it and try again.";
            return;
        }

        // No project chosen yet: refuse — never quietly save into the newest project.
        if (NeedsProject)
        {
            ResultText = ProjectWarning;
            return;
        }

        // Not wired to a project store (a bare/preview table): nothing to save into.
        if (_changes is null)
        {
            ResultText = "Saved in memory only (no project is wired up).";
            return;
        }

        var project = _projects!.Chosen!;
        var field = SelectedField.Name;
        var value = Value.Trim();
        var changed = 0;

        foreach (var row in Rows.Where(r => r.IsSelected).ToList())
        {
            var before = await CurrentValueAsync(row, field).ConfigureAwait(false);
            if (_changes.RecordEdit(row.AssetPath ?? row.Name, field, before, value, project.Id,
                                    assetPath: row.AssetPath))
            {
                changed++;
            }
        }

        ResultText = changed == 1 ? "Changed 1 file." : $"Changed {changed} files.";
    }

    /// <summary>
    /// The file's own value for <paramref name="field"/> — used as the "was …" before-value so the
    /// preview is honest. Null when there is no source, no path, or the file does not have the field.
    /// </summary>
    private async Task<string?> CurrentValueAsync(BulkRow row, string field)
    {
        if (_source is null || string.IsNullOrEmpty(row.AssetPath)) return null;

        try
        {
            var fields = await _source.FieldsAsync(row.AssetPath).ConfigureAwait(false);
            var match = fields.FirstOrDefault(f =>
                string.Equals(f.TechnicalName, field, StringComparison.OrdinalIgnoreCase));
            return match is null ? null : FieldKnowledge.FormatValue(match.CurrentValue);
        }
        catch
        {
            return null;   // an unreadable file just means no "was …" line, never a crash
        }
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsValueValid));
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnSelectedFieldChanged(FieldDefinition? value)
    {
        OnPropertyChanged(nameof(IsValueValid));
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(HasResult));

    /// <summary>Run an action on the UI thread (the rows are bound to the view).</summary>
    private static void RunOnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }
}

/// <summary>One file in the bulk table: what it is called and whether it is ticked.</summary>
public partial class BulkRow : ObservableObject
{
    public BulkRow(ItemSummary asset) => Asset = asset;

    /// <summary>The catalogue row this tick stands for.</summary>
    public ItemSummary Asset { get; }

    public string Name => Asset.Name;

    public string Category => Asset.Category;

    /// <summary>The file's own path (the change is addressed by this, never the name).</summary>
    public string? AssetPath => Asset.AssetId;

    /// <summary>True when the writer can change this file (shown under the "only changeable" tick).</summary>
    public bool Editable => Asset.Editable;

    /// <summary>Whether this file is ticked for the bulk set.</summary>
    [ObservableProperty] private bool _isSelected;
}
