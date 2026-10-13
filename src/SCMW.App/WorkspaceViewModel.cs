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
using SCMW.Core.Knowledge;

namespace SCMW.App;

/// <summary>Shows the real fields of whichever file is open, described by the field library.</summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly FieldLibrary _library;

    public WorkspaceViewModel() : this(FieldLibraryLoader.LoadDefault()) { }

    public WorkspaceViewModel(FieldLibrary library) => _library = library;

    /// <summary>The rows shown, one per real field of the open file.</summary>
    public ObservableCollection<WorkspaceField> Fields { get; } = new();

    /// <summary>The opened file's name, shown as the screen's heading.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>True when the open file has at least one field to show.</summary>
    [ObservableProperty] private bool _hasFields;

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

    /// <summary>When ticked, only files the writer can change are listed.</summary>
    [ObservableProperty] private bool _onlyEditable = true;

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
        VisibleAssets.Clear();
        foreach (var asset in AssetFilter.Apply(_allAssets, Search, SelectedCategory, OnlyEditable))
        {
            VisibleAssets.Add(asset);
        }

        OnPropertyChanged(nameof(HasAssets));
        OnPropertyChanged(nameof(HasAssetMatches));
        OnPropertyChanged(nameof(ShowAssetNoMatches));
    }

    partial void OnSearchChanged(string value) => RefreshAssets();

    partial void OnOnlyEditableChanged(bool value) => RefreshAssets();

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
    /// fields it actually has, each explained by the library where the library knows it.</summary>
    public void Show(string kind, string title, IReadOnlyList<AssetField> fields)
    {
        Title = title;
        Fields.Clear();

        foreach (var field in fields)
        {
            Fields.Add(Describe(kind, field));
        }

        HasFields = Fields.Count > 0;
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
            described: definition is not null);
    }
}

/// <summary>One row of the Workspace: a real field, described for the owner.</summary>
public sealed class WorkspaceField
{
    public WorkspaceField(string label, string unit, string value, Risk risk,
                          bool writable, string meaning, string technicalName, bool described)
    {
        Label = label;
        Unit = unit;
        Value = value;
        Risk = risk;
        Writable = writable;
        Meaning = meaning;
        TechnicalName = technicalName;
        Described = described;
    }

    /// <summary>What the owner reads.</summary>
    public string Label { get; }

    /// <summary>The unit shown beside the label (empty for a plain count).</summary>
    public string Unit { get; }

    public bool HasUnit => !string.IsNullOrWhiteSpace(Unit);

    /// <summary>The field's current value, formatted for reading.</summary>
    public string Value { get; }

    /// <summary>How risky a wrong value is — the colour of the row's dot.</summary>
    public Risk Risk { get; }

    /// <summary>True when the writer can set this field.</summary>
    public bool Writable { get; }

    /// <summary>A one-line plain meaning, or empty when not described.</summary>
    public string Meaning { get; }

    public bool HasMeaning => !string.IsNullOrWhiteSpace(Meaning);

    /// <summary>The game's own field name (used to address a change, never the display name).</summary>
    public string TechnicalName { get; }

    /// <summary>True when the field library describes this field.</summary>
    public bool Described { get; }

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
