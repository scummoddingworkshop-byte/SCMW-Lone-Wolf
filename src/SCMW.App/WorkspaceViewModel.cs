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
    private readonly INewAssetStore? _newAssets;
    private bool _loadingKind;
    private ItemSummary? _suppressingSelection;

    public WorkspaceViewModel() : this(FieldLibraryLoader.LoadDefault()) { }

    public WorkspaceViewModel(FieldLibrary library, IContentSource? source = null, ContentCatalog? catalog = null,
                              ItemChangeStore? changes = null, ProjectStore? projects = null,
                              INewAssetStore? newAssets = null)
    {
        _library = library;
        _source = source;
        _catalog = catalog;
        _changes = changes;
        _projects = projects;
        _newAssets = newAssets;

        // Choosing (or deleting) a project changes whether editing is allowed AND which new assets
        // the list shows — keep the view in step, re-reading so one project's new recipe never stays
        // on screen (and never takes edits) while another project is chosen.
        if (_projects is not null)
            _projects.Changed += OnProjectChanged;
    }

    public WorkspaceViewModel(IContentSource source) : this(FieldLibraryLoader.LoadDefault(), source) { }

    /// <summary>The rows shown, one per real field of the open file.</summary>
    public ObservableCollection<WorkspaceField> Fields { get; } = new();

    /// <summary>The opened file's name, shown as the screen's heading.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>True when the open file has at least one field to show.</summary>
    [ObservableProperty] private bool _hasFields;

    /// <summary>True when the opened file's name should show (and we are not making a new recipe).</summary>
    public bool ShowTitle => HasFields && !ShowNewRecipe;

    /// <summary>True when the "no values" line should show (and we are not making a new recipe).</summary>
    public bool ShowNoFields => !HasFields && !ShowNewRecipe;

    partial void OnHasFieldsChanged(bool value) => RefreshFileHeader();

    partial void OnShowNewRecipeChanged(bool value) => RefreshFileHeader();

    private void RefreshFileHeader()
    {
        OnPropertyChanged(nameof(ShowTitle));
        OnPropertyChanged(nameof(ShowNoFields));
    }

    // --- A recipe's ingredients (the Ingredients card) -------------------------------------------
    // A recipe stores one amount per ingredient slot per skill tier. That shape is shown as its own
    // card, so the card owns those values and they leave the plain field list. The tag and the
    // per-slot amount paths are the deliberate exception to the field-library rule — see
    // RecipeIngredients, the one place those paths are built.

    /// <summary>The skill tiers, lowest first — the order the card shows the amounts.</summary>
    private static readonly SkillLevel[] SkillLevels =
    {
        SkillLevel.NoSkill, SkillLevel.Basic, SkillLevel.Medium, SkillLevel.Advanced, SkillLevel.AboveAdvanced,
    };

    /// <summary>The game's real ingredient tags (filled when the Recipes kind is loaded).</summary>
    private IReadOnlyList<RecipeTag> _ingredientTags = Array.Empty<RecipeTag>();

    /// <summary>One row per ingredient slot of the open recipe; empty for every other kind.</summary>
    public ObservableCollection<IngredientSlotRow> IngredientSlots { get; } = new();

    /// <summary>True when the open recipe shows an Ingredients card.</summary>
    public bool HasIngredients => IngredientSlots.Count > 0;

    /// <summary>A plain note shown under the card after an ingredient change (empty when none).</summary>
    [ObservableProperty] private string _ingredientNotice = string.Empty;

    /// <summary>True when the card should show its note.</summary>
    public bool HasIngredientNotice => !string.IsNullOrWhiteSpace(IngredientNotice);

    partial void OnIngredientNoticeChanged(string value) => OnPropertyChanged(nameof(HasIngredientNotice));

    // --- A cooking dish's ingredients (the Ingredients card) -------------------------------------
    // A dish stores main and optional ingredients, each with a title and a list of foods (a soft link
    // to an item plus a few values). That shape is shown as its own card, so the card owns those
    // values and they leave the plain field list. The per-slot paths are the deliberate exception to
    // the field-library rule — see CookingIngredients, the one place those paths are built.

    /// <summary>The game's real foods (filled when the Cooking kind is loaded).</summary>
    private IReadOnlyList<CookingFood> _cookingFoods = Array.Empty<CookingFood>();

    /// <summary>One row per ingredient of the open dish; empty for every other kind.</summary>
    public ObservableCollection<CookingIngredientRow> CookingSlots { get; } = new();

    /// <summary>True when the open dish shows an Ingredients card.</summary>
    public bool HasCookingIngredients => CookingSlots.Count > 0;

    /// <summary>A plain note shown under the card after a cooking change (empty when none).</summary>
    [ObservableProperty] private string _cookingNotice = string.Empty;

    /// <summary>True when the card should show its note.</summary>
    public bool HasCookingNotice => !string.IsNullOrWhiteSpace(CookingNotice);

    partial void OnCookingNoticeChanged(string value) => OnPropertyChanged(nameof(HasCookingNotice));

    // The arguments of the last Show, so an ingredient change can redraw the card without re-reading
    // the game file (the file itself never changes; only the project's records do).
    private string _shownKind = string.Empty;
    private string _shownTitle = string.Empty;
    private IReadOnlyList<AssetField> _shownFields = Array.Empty<AssetField>();
    private string? _shownAssetPath;
    private IReadOnlyList<AddableField>? _shownAddable;

    // --- Which mod you are editing (Task 4.1) ----------------------------------------------------
    // "One project = one mod": an edit is saved into the project the player picked. With none
    // picked, the Workspace refuses (and says so) rather than quietly targeting the newest project.

    /// <summary>True when a project store is wired but the player has not chosen a project yet.</summary>
    public bool NeedsProject => _changes is not null && !(_projects?.HasChosenProject ?? false);

    /// <summary>Plain words shown when <see cref="NeedsProject"/> — never jargon.</summary>
    public string ProjectWarning =>
        "Choose a project first — that's the mod your changes are saved into.";

    /// <summary>
    /// The chosen project changed (or was deleted). Update the editing gate, then re-read the open
    /// kind: each project's new assets live in its own file, so the list must follow the choice — a
    /// recipe made in one project must never stay on screen (and take edits) under another.
    /// </summary>
    private void OnProjectChanged()
    {
        OnPropertyChanged(nameof(NeedsProject));

        // Only a real switch of project needs a re-read; a rename or a description/picture edit
        // (which also announce a change) must not reload the list.
        var id = _projects?.Chosen?.Id;
        if (string.Equals(id, _shownProjectId, StringComparison.Ordinal)) return;
        _shownProjectId = id;

        if (!string.IsNullOrEmpty(_kind))
            _ = LoadKindAsync(_kind);
    }

    /// <summary>The chosen project's id the list was last built for, so a real switch is detected.</summary>
    private string? _shownProjectId;

    // --- The kind picker (Task 3.0) --------------------------------------------------------------
    // Only the kinds we have released for browsing. Loot & spawn is built, but not offered yet.

    /// <summary>The kind the Workspace opens on — the commonest thing to change.</summary>
    public const string DefaultKind = "Items";

    /// <summary>The kinds the picker offers, in the order they are shown.</summary>
    public IReadOnlyList<string> Kinds { get; } = new[] { "Items", "Base building", "Recipes", "Cooking" };

    /// <summary>The kind being browsed (always one of <see cref="Kinds"/>).</summary>
    [ObservableProperty] private string _selectedKind = DefaultKind;

    partial void OnSelectedKindChanged(string value)
    {
        OnPropertyChanged(nameof(ShowNewRecipeButton));
        OnPropertyChanged(nameof(NewButtonText));
        OnPropertyChanged(nameof(NewPanelTitle));
        if (!ShowNewRecipeButton) ShowNewRecipe = false;   // the panel belongs to recipes and items

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
        "Cooking" => AssetFamily.Cooking,
        _ => AssetFamily.Items,
    };

    /// <summary>Read the picked kind's real files and show them. The open file is closed first, as it
    /// may belong to the kind we just left. Never throws.</summary>
    private async System.Threading.Tasks.Task LoadKindAsync(string kind)
    {
        var family = FamilyOf(kind);

        IReadOnlyList<ItemSummary> assets = Array.Empty<ItemSummary>();
        _ingredientTags = Array.Empty<RecipeTag>();
        _cookingFoods = Array.Empty<CookingFood>();
        if (_source is not null && _catalog is not null)
        {
            try
            {
                var all = await AssetReader.ReadAllAsync(_source).ConfigureAwait(false);
                assets = _catalog.Map(family, all);
                if (family == AssetFamily.Recipes)
                {
                    _ingredientTags = RecipeTags.From(all);
                    assets = WithNewAssetRows(assets);   // this project's own new recipes go on top
                }
                else if (family == AssetFamily.Cooking)
                    _cookingFoods = CookingFoods.From(all);
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

        var ids = assets.Where(a => a.ReadPath is null && !string.IsNullOrEmpty(a.AssetId))
                        .Select(a => a.AssetId!).ToList();
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
        OnPropertyChanged(nameof(ShowNewRecipeButton));   // the "New" button needs the kind actually loaded
        OnPropertyChanged(nameof(NewButtonText));
        OnPropertyChanged(nameof(NewPanelTitle));
        RefreshAssets();
    }

    private void RefreshAssets()
    {
        VisibleAssets.Clear();
        foreach (var asset in AssetFilter.Apply(_allAssets, Search, SelectedCategory))
        {
            VisibleAssets.Add(asset);
        }

        OnPropertyChanged(nameof(HasAssets));
        OnPropertyChanged(nameof(HasAssetMatches));
        OnPropertyChanged(nameof(ShowAssetNoMatches));
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

    // --- Creating a new asset (Task 7.5, extended in Phase 8) -------------------------------------
    // The one place a project gains a brand-new recipe or item: the owner types a plain name, picks a
    // game file to start from, and the app records it. The new asset then shows in the list like any
    // other and is edited the same way — its values are read from the donor but saved against its own
    // path, so the build writes them onto the clone.

    /// <summary>The kind of new asset the open list creates: items from the Items list, recipes from
    /// the Recipes list.</summary>
    public NewAssetKind CurrentNewKind =>
        IsLoadedKind("Items") ? NewAssetKind.Item : NewAssetKind.Recipe;

    /// <summary>True when the open kind can create new content (recipes or items) AND that kind's list
    /// has actually loaded — so a quick kind switch never leaves the panel offering the previous kind's
    /// files as donors.</summary>
    public bool ShowNewRecipeButton => IsLoadedKind("Recipes") || IsLoadedKind("Items");

    /// <summary>True when the owner is browsing <paramref name="kind"/> and it has finished loading.</summary>
    private bool IsLoadedKind(string kind) =>
        string.Equals(SelectedKind, kind, StringComparison.Ordinal)
        && string.Equals(_kind, kind, StringComparison.Ordinal);

    /// <summary>The "New" button's words, for the open kind.</summary>
    public string NewButtonText => CurrentNewKind == NewAssetKind.Item ? "＋ New item" : "＋ New recipe";

    /// <summary>The panel's heading, for the open kind.</summary>
    public string NewPanelTitle => CurrentNewKind == NewAssetKind.Item ? "New item" : "New recipe";

    /// <summary>True while the "New" panel is showing in place of a file's values.</summary>
    [ObservableProperty] private bool _showNewRecipe;

    /// <summary>The plain name the owner typed for the new asset (e.g. "Olive Press").</summary>
    [ObservableProperty] private string _newRecipeName = string.Empty;

    /// <summary>The game file the new one is copied from, or null.</summary>
    [ObservableProperty] private DonorChoice? _newRecipeDonor;

    /// <summary>The files the owner can start from (the game's own, never a new asset).</summary>
    public ObservableCollection<DonorChoice> RecipeDonors { get; } = new();

    /// <summary>A plain line telling the owner what needs fixing before the asset can be made.</summary>
    [ObservableProperty] private string _newRecipeNotice = string.Empty;

    /// <summary>True when there is something to say about making the asset.</summary>
    public bool HasNewRecipeNotice => !string.IsNullOrWhiteSpace(NewRecipeNotice);

    partial void OnNewRecipeNoticeChanged(string value) => OnPropertyChanged(nameof(HasNewRecipeNotice));

    /// <summary>Open the "New" panel: fill the "start from" list from the open kind's files.</summary>
    [RelayCommand]
    private void OpenNewRecipe()
    {
        if (!ShowNewRecipeButton) return;   // the list must be loaded before it can be used

        FillRecipeDonors();
        var what = CurrentNewKind == NewAssetKind.Item ? "items" : "recipes";
        NewRecipeNotice = RecipeDonors.Count == 0
            ? $"No {what} were found to start from. Check your SCUM folder in Settings."
            : string.Empty;
        ShowNewRecipe = true;
    }

    /// <summary>Close the panel without making anything.</summary>
    [RelayCommand]
    private void CancelNewRecipe()
    {
        ShowNewRecipe = false;
        NewRecipeNotice = string.Empty;
    }

    /// <summary>
    /// Record one brand-new asset from the typed name and the picked donor, then open it for editing.
    /// Refused (nothing recorded) with a plain reason when there is no project, no donor, no usable
    /// name, or the name is already taken.
    /// </summary>
    [RelayCommand]
    private void CreateNewRecipe()
    {
        var kind = CurrentNewKind;
        var word = kind == NewAssetKind.Item ? "item" : "recipe";

        if (NeedsProject)
        {
            NewRecipeNotice = ProjectWarning;
            return;
        }

        var chosen = _projects?.Chosen;
        if (_newAssets is null || chosen is null) return;

        if (NewRecipeDonor is not { } donor || !IsKnownDonor(donor))
        {
            NewRecipeNotice = $"Pick a {word} to start from.";
            return;
        }

        var name = kind == NewAssetKind.Item
            ? NewAssetNaming.ItemName(NewRecipeName)
            : NewAssetNaming.RecipeName(NewRecipeName);
        if (name is null)
        {
            NewRecipeNotice = $"Type a name for the new {word}.";
            return;
        }

        // The name must be free on the loaded list AND in the project's own record — a project switch
        // leaves the list stale, so the record is the truth. Names are compared loosely, so "Olive Oil",
        // "OliveOil" and "olive_oil" all count as the same name.
        var key = NormalisedName(name);
        var taken = _allAssets.Any(a => NormalisedName(a.Name) == key)
                    || _newAssets.Load(chosen.FolderPath).Any(a => NormalisedName(a.Name) == key);
        if (taken)
        {
            NewRecipeNotice = $"There's already a {word} called {name} — try another name.";
            return;
        }

        var asset = new NewAsset(
            Guid.NewGuid(), name, kind, donor.Name, donor.Path,
            NewAssetNaming.FolderFor(FolderOf(donor.Path), chosen.Name), DateTime.UtcNow);

        if (!_newAssets.Add(chosen.FolderPath, asset))
        {
            NewRecipeNotice = $"The new {word} could not be saved. Please try again.";
            return;
        }

        var row = NewRecipeRow(asset);
        _allAssets = new[] { row }.Concat(_allAssets).ToList();
        ShowNewRecipe = false;
        NewRecipeName = string.Empty;
        NewRecipeNotice = string.Empty;

        Browse(_kind, _allAssets);
        _ = OpenAsync(row);
    }

    /// <summary>The picker's choices: the game's own files of the open kind (a new asset is never
    /// offered as a donor). An item's donor is the item package; the build derives its "_ES" twin.</summary>
    private void FillRecipeDonors()
    {
        RecipeDonors.Clear();
        foreach (var asset in _allAssets.Where(a => a.ReadPath is null))
        {
            var path = DonorPathOf(asset);
            if (!string.IsNullOrEmpty(path))
                RecipeDonors.Add(new DonorChoice(asset.Name, path!));
        }
    }

    /// <summary>The path a donor is cloned from: an ordinary file itself; for an item, the item package
    /// behind the "_ES" file the list shows (the build derives the twin from it).</summary>
    private string? DonorPathOf(ItemSummary asset)
    {
        if (CurrentNewKind != NewAssetKind.Item) return asset.AssetId;
        return ClassFileOf(asset.AssetId) ?? asset.AssetId;
    }

    /// <summary>True when the choice is one of the game's own files in the loaded list — never a
    /// brand-new asset, and never a file from a stale list.</summary>
    private bool IsKnownDonor(DonorChoice donor) =>
        _allAssets.Any(a => a.ReadPath is null
                            && string.Equals(DonorPathOf(a), donor.Path, StringComparison.OrdinalIgnoreCase));

    /// <summary>A name with only its letters and digits, lower-cased — so "Olive Oil", "OliveOil" and
    /// "olive_oil" count as the same name when checking for a clash.</summary>
    private static string NormalisedName(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>This project's recorded new recipes as list rows, on top of the game's own recipes.</summary>
    private IReadOnlyList<ItemSummary> WithNewAssetRows(IReadOnlyList<ItemSummary> recipes)
    {
        var folder = _projects?.Chosen?.FolderPath;
        if (_newAssets is null || folder is null) return recipes;

        var rows = _newAssets.Load(folder).Select(NewRecipeRow).ToList();
        return rows.Count == 0 ? recipes : rows.Concat(recipes).ToList();
    }

    /// <summary>A list row for a new asset: it reads the donor's values but is saved against its own
    /// path (an item's editable file is its <c>_ES</c> twin, so both read and write use the twin).</summary>
    private static ItemSummary NewRecipeRow(NewAsset asset) =>
        new(asset.Name, "New",
            $"New {KindWord(asset.Kind)} — copied from {asset.DonorName}", editable: true,
            assetId: asset.EditPath, readPath: asset.TwinDonorPath ?? asset.DonorPath);

    /// <summary>The plain word for a new asset's kind.</summary>
    private static string KindWord(NewAssetKind kind) => kind == NewAssetKind.Item ? "item" : "recipe";

    /// <summary>The folder a path lives in (everything before the last slash).</summary>
    private static string FolderOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash > 0 ? path[..slash] : string.Empty;
    }

    /// <summary>Open the "New recipe" panel from the front door: load the Recipes kind first.</summary>
    public async System.Threading.Tasks.Task OpenNewRecipeAsync()
    {
        await OpenKindAsync("Recipes").ConfigureAwait(false);
        RunOnUi(() => OpenNewRecipe());
    }

    /// <summary>True when the open file is a brand-new recipe this project made (so it can be removed).
    /// An ordinary game recipe reads itself (<c>ReadPath</c> is null) and is never removable here.</summary>
    public bool IsOpenNewRecipe => SelectedAsset?.ReadPath is not null;

    /// <summary>True while the "remove this new recipe?" confirmation is showing.</summary>
    [ObservableProperty] private bool _confirmingRemoveNewRecipe;

    /// <summary>Arm the removal of the open new recipe (a second click confirms).</summary>
    [RelayCommand] private void RequestRemoveNewRecipe() => ConfirmingRemoveNewRecipe = true;

    /// <summary>Back out of removing the open new recipe.</summary>
    [RelayCommand] private void CancelRemoveNewRecipe() => ConfirmingRemoveNewRecipe = false;

    /// <summary>
    /// Delete the open new recipe: drop its edits and its own record, so nothing orphaned is left
    /// behind (an orphan edit is exactly what a build cannot read). Refused — nothing removed — when
    /// the open file is not a new recipe, or no project is chosen.
    /// </summary>
    [RelayCommand]
    private void ConfirmRemoveNewRecipe()
    {
        ConfirmingRemoveNewRecipe = false;

        var folder = _projects?.Chosen?.FolderPath;
        var asset = SelectedAsset;
        if (folder is null || _changes is null || _newAssets is null
            || asset?.ReadPath is null || string.IsNullOrEmpty(asset.AssetId))
        {
            return;
        }

        // Drop the edits first, then the record itself — never leave a record-less change behind.
        _changes.RemoveItem(folder, asset.AssetId!);

        var id = _newAssets.Load(folder)
            .FirstOrDefault(a => string.Equals(a.PackagePath, asset.AssetId, StringComparison.OrdinalIgnoreCase))?.Id;
        if (id is { } assetId) _newAssets.Undo(folder, assetId);

        _ = LoadKindAsync(_kind);   // rebuild the list without it
    }

    /// <summary>Open <paramref name="kind"/>'s file named <paramref name="title"/> and show the
    /// fields it actually has, each explained by the library where the library knows it.
    /// <paramref name="addable"/> lists the described fields the file lacks that we could add by
    /// borrowing a donor item's shape (Task 5.2); it is empty for a caller that has not looked.</summary>
    public void Show(string kind, string title, IReadOnlyList<AssetField> fields, string? assetPath = null,
                     IReadOnlyList<AddableField>? addable = null, IReadOnlyList<RecipeTag>? ingredientTags = null,
                     IReadOnlyList<CookingFood>? cookingFoods = null)
    {
        if (ingredientTags is not null) _ingredientTags = ingredientTags;
        if (cookingFoods is not null) _cookingFoods = cookingFoods;

        var sameAsset = string.Equals(_shownAssetPath, assetPath, StringComparison.OrdinalIgnoreCase);
        _shownKind = kind;
        _shownTitle = title;
        _shownFields = fields;
        _shownAssetPath = assetPath;
        _shownAddable = addable;
        if (!sameAsset) { IngredientNotice = string.Empty; CookingNotice = string.Empty; }

        Title = title;
        Fields.Clear();
        IngredientSlots.Clear();
        CookingSlots.Clear();

        // A recipe's ingredient slots own their tag and amounts, and a dish's own the title and the
        // foods, so those move into the cards and stay out of the plain (locked) list. Every other
        // field of the file shows exactly as before.
        var fileSlots = string.Equals(kind, "Recipes", StringComparison.OrdinalIgnoreCase)
            ? RecipeIngredients.From(fields)
            : Array.Empty<IngredientSlot>();
        var cookingSlots = string.Equals(kind, "Cooking", StringComparison.OrdinalIgnoreCase)
            ? CookingIngredients.From(fields)
            : Array.Empty<CookingIngredientSlot>();
        var owned = OwnedIngredientPaths(fileSlots);
        foreach (var path in OwnedCookingPaths(cookingSlots)) owned.Add(path);

        foreach (var field in fields)
        {
            if (owned.Contains(field.TechnicalName)) continue;
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

        // A field the project ADDED to this item shows as its own row too, with the value it was
        // given, so it can be seen and changed here — not only in the "fields you could add" card.
        if (!string.IsNullOrEmpty(assetPath))
        {
            foreach (var record in AddedRecordsFor(assetPath))
                Fields.Add(AddedRow(record));
        }

        HasFields = Fields.Count > 0;

        // The Ingredients card (recipes only): the file's slots with the project's list changes played
        // back, one row each, with recorded values applied.
        var displayed = RecipeIngredients.WithListChanges(fileSlots, ListChanges(assetPath));
        if (displayed.Count > 0)
        {
            BuildIngredientRows(displayed, assetPath, title);
        }
        OnPropertyChanged(nameof(HasIngredients));

        // The Ingredients card (dishes only): the file's ingredients with the project's list changes
        // played back, one row each, with recorded values applied.
        var displayedCooking = CookingIngredients.WithListChanges(cookingSlots, CookingListChanges(assetPath));
        if (displayedCooking.Count > 0)
        {
            BuildCookingRows(displayedCooking, assetPath, title);
        }
        OnPropertyChanged(nameof(HasCookingIngredients));

        AddableFields.Clear();
        if (addable is not null)
        {
            foreach (var field in addable) AddableFields.Add(field);
        }
        OnPropertyChanged(nameof(HasAddableFields));
    }

    /// <summary>The field paths the Ingredients card owns (each slot's tag and amounts).</summary>
    private static HashSet<string> OwnedIngredientPaths(IReadOnlyList<IngredientSlot> slots)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in slots)
        {
            if (slot.Tag is not null) owned.Add(slot.Tag.TechnicalName);
            foreach (var amount in slot.Amounts) owned.Add(amount.Field.TechnicalName);
        }

        return owned;
    }

    /// <summary>The project's recorded add/remove changes to this recipe's ingredient list, in order.</summary>
    private IReadOnlyList<IngredientListChange> ListChanges(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return Array.Empty<IngredientListChange>();

        var changes = new List<IngredientListChange>();
        foreach (var record in Records())
        {
            if (!string.Equals(record.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (record.Operation == ChangeOperation.AddEntry
                && string.Equals(record.FieldTechnicalName, RecipeIngredients.ListName, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new IngredientListChange(ChangeOperation.AddEntry, -1));
            }
            else if (record.Operation == ChangeOperation.RemoveEntry
                     && RecipeIngredients.TrySlotIndex(record.FieldTechnicalName, out var index))
            {
                changes.Add(new IngredientListChange(ChangeOperation.RemoveEntry, index));
            }
        }

        return changes;
    }

    /// <summary>Redraw the open file from the records already known (no game-file read).</summary>
    private void RefreshShown() =>
        Show(_shownKind, _shownTitle, _shownFields, _shownAssetPath, _shownAddable);

    /// <summary>Build one card row per slot, applying the project's recorded values by path.</summary>
    private void BuildIngredientRows(IReadOnlyList<IngredientSlot> slots, string? assetPath, string title)
    {
        var records = Records();

        foreach (var slot in slots)
        {
            var tagName = slot.TagValue;
            var tagPath = RecipeIngredients.TagPath(slot.Index);
            var originalTag = tagName is null || string.IsNullOrEmpty(assetPath)
                ? tagName
                : AppliedValue.OriginalForAsset(records, assetPath, title, tagPath) ?? tagName;

            var amounts = new List<WorkspaceField>(SkillLevels.Length);
            foreach (var level in SkillLevels)
            {
                var path = RecipeIngredients.AmountPath(slot.Index, level);
                var gameValue = slot.AmountField(level)?.CurrentValue ?? string.Empty;
                var applied = string.IsNullOrEmpty(assetPath)
                    ? gameValue
                    : AppliedValue.ResolveForAsset(records, assetPath, title, path, gameValue) ?? gameValue;
                var original = string.IsNullOrEmpty(assetPath)
                    ? gameValue
                    : AppliedValue.OriginalForAsset(records, assetPath, title, path) ?? gameValue;

                var field = new WorkspaceField(
                    label: SkillLabel(level), unit: string.Empty, value: gameValue,
                    risk: Risk.Unknown, writable: true, meaning: string.Empty,
                    technicalName: path, described: false, type: FieldType.Number);
                field.Load(applied, original);
                amounts.Add(field);
            }

            IngredientSlots.Add(new IngredientSlotRow(slot.Index, TagChoice(tagName, _ingredientTags), originalTag,
                                                      _ingredientTags, amounts));
        }
    }

    /// <summary>The picker's entry for a tag name: the indexed tag, or a plainly-labelled stand-in.</summary>
    private static RecipeTag? TagChoice(string? name, IReadOnlyList<RecipeTag> choices)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var choice in choices)
        {
            if (string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase)) return choice;
        }

        return new RecipeTag(name, RecipeTags.Label(name));
    }

    /// <summary>The owner-facing label for a skill tier.</summary>
    private static string SkillLabel(SkillLevel level) => level switch
    {
        SkillLevel.NoSkill => "No skill",
        SkillLevel.AboveAdvanced => "Above advanced",
        _ => RecipeIngredients.SkillName(level),
    };

    // --- A dish's ingredients (the Ingredients card) ---------------------------------------------

    /// <summary>The field paths the cooking Ingredients card owns (each title, food item and value).</summary>
    private static IEnumerable<string> OwnedCookingPaths(IReadOnlyList<CookingIngredientSlot> ingredients)
    {
        foreach (var ingredient in ingredients)
        {
            if (ingredient.Title is not null)
                yield return CookingIngredients.TitlePath(ingredient.IsMain, ingredient.Index);

            foreach (var food in ingredient.Foods)
            {
                if (food.Item is not null)
                    yield return CookingIngredients.FoodItemPath(ingredient.IsMain, ingredient.Index, food.Index);

                if (food.ResourceType is not null)
                    yield return CookingIngredients.FoodResourcePath(ingredient.IsMain, ingredient.Index, food.Index);

                foreach (var value in food.Values)
                    yield return CookingIngredients.FoodValuePath(ingredient.IsMain, ingredient.Index, food.Index, value.Name);
            }
        }
    }

    /// <summary>The project's recorded add/remove changes to this dish's ingredient and food lists, in order.</summary>
    private IReadOnlyList<CookingListChange> CookingListChanges(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return Array.Empty<CookingListChange>();

        var changes = new List<CookingListChange>();
        foreach (var record in Records())
        {
            if (!string.Equals(record.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (record.Operation is not (ChangeOperation.AddEntry or ChangeOperation.RemoveEntry))
                continue;

            if (CookingIngredients.TryListPath(record.FieldTechnicalName, out var path))
                changes.Add(new CookingListChange(record.Operation, path.IsMain, path.IngredientIndex,
                                                  path.IsFood, path.FoodIndex));
        }

        return changes;
    }

    /// <summary>Build one card row per ingredient (and its foods), applying recorded values by path.</summary>
    private void BuildCookingRows(IReadOnlyList<CookingIngredientSlot> ingredients, string? assetPath, string title)
    {
        var records = Records();

        foreach (var ingredient in ingredients)
        {
            var isMain = ingredient.IsMain;
            var index = ingredient.Index;

            WorkspaceField? titleField = null;
            if (ingredient.Title is not null)
            {
                var path = CookingIngredients.TitlePath(isMain, index);
                var gameValue = ingredient.Title.CurrentValue;
                var applied = string.IsNullOrEmpty(assetPath)
                    ? gameValue
                    : AppliedValue.ResolveForAsset(records, assetPath, title, path, gameValue) ?? gameValue;
                var original = string.IsNullOrEmpty(assetPath)
                    ? gameValue
                    : AppliedValue.OriginalForAsset(records, assetPath, title, path) ?? gameValue;

                titleField = new WorkspaceField(
                    label: "Title", unit: string.Empty, value: gameValue,
                    risk: Risk.Unknown, writable: true, meaning: string.Empty,
                    technicalName: path, described: false, type: FieldType.Text);
                titleField.Load(applied, original);
            }

            var foods = new List<CookingFoodRow>(ingredient.Foods.Count);
            foreach (var food in ingredient.Foods)
            {
                var itemPath = CookingIngredients.FoodItemPath(isMain, index, food.Index);
                var gameItem = food.ItemValue;
                var appliedItem = gameItem is null || string.IsNullOrEmpty(assetPath)
                    ? gameItem
                    : AppliedValue.ResolveForAsset(records, assetPath, title, itemPath, gameItem) ?? gameItem;
                var originalItem = gameItem is null || string.IsNullOrEmpty(assetPath)
                    ? gameItem
                    : AppliedValue.OriginalForAsset(records, assetPath, title, itemPath) ?? gameItem;

                var values = new List<WorkspaceField>(food.Values.Count);
                foreach (var value in food.Values)
                {
                    var path = CookingIngredients.FoodValuePath(isMain, index, food.Index, value.Name);
                    var gameValue = value.Field.CurrentValue;
                    var applied = string.IsNullOrEmpty(assetPath)
                        ? gameValue
                        : AppliedValue.ResolveForAsset(records, assetPath, title, path, gameValue) ?? gameValue;
                    var original = string.IsNullOrEmpty(assetPath)
                        ? gameValue
                        : AppliedValue.OriginalForAsset(records, assetPath, title, path) ?? gameValue;

                    var field = new WorkspaceField(
                        label: FoodValueLabel(value.Name), unit: string.Empty, value: gameValue,
                        risk: Risk.Unknown, writable: true, meaning: string.Empty,
                        technicalName: path, described: false, type: FoodValueType(value.Name));
                    field.Load(applied, original);
                    values.Add(field);
                }

                foods.Add(new CookingFoodRow(isMain, index, food.Index, originalItem, appliedItem,
                                             food.HasItem, food.ResourceName,
                                             food.HasItem ? FoodChoice(appliedItem, _cookingFoods) : null,
                                             _cookingFoods, values));
            }

            CookingSlots.Add(new CookingIngredientRow(isMain, index, titleField, foods));
        }
    }

    /// <summary>The picker's entry for a soft path: the indexed food, or a plainly-labelled stand-in.</summary>
    private static CookingFood? FoodChoice(string? softPath, IReadOnlyList<CookingFood> choices)
    {
        if (string.IsNullOrEmpty(softPath)) return null;

        foreach (var choice in choices)
        {
            if (string.Equals(choice.SoftPath, softPath, StringComparison.OrdinalIgnoreCase)) return choice;
        }

        return new CookingFood(softPath, CookingFoods.Label(softPath));
    }

    /// <summary>The owner-facing label for a food value.</summary>
    private static string FoodValueLabel(string name) => name switch
    {
        "NutrientInclusionFactor" => "Nutrient inclusion",
        "ShouldConsumeWhole" => "Consume whole",
        "IsResource" => "Is resource",
        _ => name,
    };

    /// <summary>The kind of a food value, so its row edits and validates like any other value.</summary>
    private static FieldType FoodValueType(string name) =>
        name is "ShouldConsumeWhole" or "IsResource" ? FieldType.Boolean : FieldType.Number;

    /// <summary>The project's "add a field" records for this item (its own file or its class file).</summary>
    private IEnumerable<ChangeRecord> AddedRecordsFor(string assetPath) =>
        Records().Where(r => r.Operation == ChangeOperation.AddField
                             && IsTargetOf(r, assetPath, ClassFileOf(assetPath)));

    /// <summary>A field row for a field the project added to the item, described by the library.</summary>
    private WorkspaceField AddedRow(ChangeRecord record)
    {
        var definition = _library.Lookup(_kind, record.FieldTechnicalName);
        return new WorkspaceField(
            label: definition?.Label ?? FieldNames.Friendly(record.FieldTechnicalName),
            unit: definition?.Unit ?? string.Empty,
            value: record.NewValue,
            risk: definition?.Risk ?? Risk.Unknown,
            writable: true,
            meaning: definition?.Meaning ?? string.Empty,
            technicalName: record.FieldTechnicalName,
            described: definition is not null,
            type: definition?.Type ?? FieldType.Unknown)
        {
            IsAdded = true,
            AddRecordId = record.Id,
        };
    }

    /// <summary>Rebuild just the "added field" rows in the list from the project's records.</summary>
    private void RefreshAddedFieldRows()
    {
        for (var i = Fields.Count - 1; i >= 0; i--)
        {
            if (Fields[i].IsAdded) Fields.RemoveAt(i);
        }

        var path = SelectedAsset?.AssetId;
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var record in AddedRecordsFor(path))
                Fields.Add(AddedRow(record));
        }

        HasFields = Fields.Count > 0;
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
        OnPropertyChanged(nameof(IsOpenNewRecipe));
        ConfirmingRemoveNewRecipe = false;   // a new selection never keeps a pending removal armed

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
                // A brand-new asset reads its donor's values; every ordinary file reads itself.
                var readPath = asset.ReadPath ?? asset.AssetId ?? asset.Description;
                fields = await _source.FieldsAsync(readPath).ConfigureAwait(false);
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

        var addedRecords = Records()
            .Where(r => r.Operation == ChangeOperation.AddField && IsTargetOf(r, assetPath, classPath))
            .ToList();

        var candidates = _library.ForKind(kind)
            .Where(d => d.Addable && !presentNames.Contains(d.Name))
            .ToList();

        var addable = new List<AddableField>();
        foreach (var definition in candidates)
        {
            var added = addedRecords.FirstOrDefault(r =>
                string.Equals(r.FieldTechnicalName, definition.Name, StringComparison.OrdinalIgnoreCase));

            if (added is not null)
            {
                // This project already added the field: show it as added, with a Remove option.
                addable.Add(new AddableField(definition.Label, definition.Unit, definition.Meaning,
                                             definition.Name, added.DonorPath ?? "", definition.Type,
                                             added.AssetPath ?? assetPath, isAdded: true, recordId: added.Id));
                continue;
            }

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
        var id = _changes.RecordAddField(target, field.TechnicalName, field.Draft.Trim(), field.DonorPath,
                                         _projects!.Chosen!.Id, assetPath: target);

        // The field is now part of the project: show it as added, with a Remove option.
        if (id is { } recordId) field.MarkAdded(recordId);

        // And show it in the item's own field list too.
        RefreshAddedFieldRows();
    }

    /// <summary>Remove a field this project added to the item (undo its add record).</summary>
    [RelayCommand]
    private void Remove(AddableField? field)
    {
        if (field?.RecordId is not { } id) return;

        var folder = _projects?.Chosen?.FolderPath;
        if (_changes is null || folder is null) return;

        _changes.Undo(folder, id);
        field.MarkRemoved();

        // And drop its row from the item's field list.
        RefreshAddedFieldRows();
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

        // An added field's value lives in its "add" record — edit that record, never add a separate Set.
        if (field.IsAdded && field.AddRecordId is { } addId)
        {
            var addedFolder = _projects?.Chosen?.FolderPath;
            field.Value = draft;
            field.IsEditing = false;
            if (draft != field.OriginalValue && addedFolder is not null)
                _changes.UpdateAddField(addedFolder, addId, draft);
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

    /// <summary>Change the tag an ingredient slot accepts. Recorded against the slot's own tag path
    /// (a deliberate exception to the field-library rule — see <see cref="RecipeIngredients"/>).</summary>
    [RelayCommand]
    private void SetIngredientTag(IngredientSlotRow? slot)
    {
        if (slot?.SelectedTag is null) return;

        var chosen = slot.SelectedTag.Name;
        if (string.Equals(chosen, slot.OriginalTagName, StringComparison.OrdinalIgnoreCase))
            return;   // the file's own tag (or the picker settling on load): nothing to record

        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path)) return;   // not wired to a project: show only
        if (NeedsProject) return;   // no project chosen: refuse (the banner says why)

        _changes.RecordEdit(path!, RecipeIngredients.TagPath(slot.Index), slot.OriginalTagName, chosen,
                            _projects!.Chosen!.Id, assetPath: path);
    }

    /// <summary>Add an ingredient slot. Recorded as a list change; the build clones the last slot, so
    /// the card shows the new (cloned) row at once.</summary>
    [RelayCommand]
    private void AddIngredientSlot()
    {
        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path) || NeedsProject) return;

        _changes.RecordListChange(path!, RecipeIngredients.ListName, ChangeOperation.AddEntry,
                                  _projects!.Chosen!.Id, assetPath: path);
        IngredientNotice = string.Empty;
        RefreshShown();
    }

    /// <summary>Remove an ingredient slot at its own index. Removing shifts the rest, so the recipe's
    /// existing value changes are cleared first — the card says how many, so nothing is silent.</summary>
    [RelayCommand]
    private void RemoveIngredientSlot(IngredientSlotRow? slot)
    {
        if (slot is null) return;

        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path) || NeedsProject) return;

        var dropped = _changes.RecordListChange(path!, RecipeIngredients.SlotPath(slot.Index),
                                                ChangeOperation.RemoveEntry, _projects!.Chosen!.Id, assetPath: path);

        IngredientNotice = dropped > 0
            ? $"{dropped} other change(s) on this recipe were cleared, because removing an ingredient renumbers the rest — please re-check them."
            : string.Empty;
        RefreshShown();
    }

    /// <summary>Change the item a food points at. Recorded against the food's own item path (a
    /// deliberate exception to the field-library rule — see <see cref="CookingIngredients"/>). Picking
    /// the file's own item back is a revert, so the recorded change is dropped rather than left to build.</summary>
    [RelayCommand]
    private void SetCookingFood(CookingFoodRow? food)
    {
        if (food?.SelectedFood is null) return;

        var chosen = food.SelectedFood.SoftPath;
        if (string.Equals(chosen, food.CurrentItemPath, StringComparison.OrdinalIgnoreCase))
            return;   // the picker settling on what is already shown: nothing to record

        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path)) return;   // not wired to a project: show only
        if (NeedsProject) return;   // no project chosen: refuse (the banner says why)

        var fieldPath = CookingIngredients.FoodItemPath(food.IsMain, food.IngredientIndex, food.Index);

        if (string.Equals(chosen, food.OriginalItemPath, StringComparison.OrdinalIgnoreCase))
        {
            UndoCookingSet(path!, fieldPath);   // back to the game's own item: drop the standing change
            food.CurrentItemPath = food.OriginalItemPath;
            return;
        }

        _changes.RecordEdit(path!, fieldPath, food.OriginalItemPath, chosen, _projects!.Chosen!.Id,
                            assetPath: path);
        food.CurrentItemPath = chosen;
    }

    /// <summary>Drop every recorded value change on one dish field (used when a picker returns to the
    /// game's own value, so the change is truly gone rather than a no-op record the build would still write).</summary>
    private void UndoCookingSet(string assetPath, string fieldPath)
    {
        var folder = _projects?.Chosen?.FolderPath;
        if (_changes is null || string.IsNullOrEmpty(folder)) return;

        var ids = _changes.For(folder)
            .Where(r => r.Operation == ChangeOperation.Set
                        && string.Equals(r.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.FieldTechnicalName, fieldPath, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .ToList();

        foreach (var id in ids) _changes.Undo(folder, id);
    }

    /// <summary>Add a food to an ingredient. Recorded as a list change; the build clones the last food,
    /// so the card shows the new (cloned) row at once.</summary>
    [RelayCommand]
    private void AddCookingFood(CookingIngredientRow? ingredient)
    {
        if (ingredient is null) return;

        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path) || NeedsProject) return;

        _changes.RecordListChange(path!, CookingIngredients.FoodListPath(ingredient.IsMain, ingredient.Index),
                                  ChangeOperation.AddEntry, _projects!.Chosen!.Id, assetPath: path);
        CookingNotice = string.Empty;
        RefreshShown();
    }

    /// <summary>Remove a food at its own index. Removing shifts the rest, so the dish's existing value
    /// changes are cleared first — the card says how many, so nothing is silent.</summary>
    [RelayCommand]
    private void RemoveCookingFood(CookingFoodRow? food)
    {
        if (food is null) return;

        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path) || NeedsProject) return;

        var dropped = _changes.RecordListChange(path!,
            CookingIngredients.FoodPath(food.IsMain, food.IngredientIndex, food.Index),
            ChangeOperation.RemoveEntry, _projects!.Chosen!.Id, assetPath: path);

        CookingNotice = dropped > 0
            ? $"{dropped} other change(s) on this dish were cleared, because removing a food renumbers the rest — please re-check them."
            : string.Empty;
        RefreshShown();
    }

    /// <summary>Add a main ingredient. Recorded as a list change; the build clones the last one.</summary>
    [RelayCommand]
    private void AddCookingMainIngredient() => AddCookingIngredient(isMain: true);

    /// <summary>Add an optional ingredient. Recorded as a list change; the build clones the last one.</summary>
    [RelayCommand]
    private void AddCookingOptionalIngredient() => AddCookingIngredient(isMain: false);

    private void AddCookingIngredient(bool isMain)
    {
        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path) || NeedsProject) return;

        _changes.RecordListChange(path!, CookingIngredients.ListName(isMain), ChangeOperation.AddEntry,
                                  _projects!.Chosen!.Id, assetPath: path);
        CookingNotice = string.Empty;
        RefreshShown();
    }

    /// <summary>Remove an ingredient at its own index. Removing shifts the rest, so the dish's existing
    /// value changes are cleared first — the card says how many, so nothing is silent.</summary>
    [RelayCommand]
    private void RemoveCookingIngredient(CookingIngredientRow? ingredient)
    {
        if (ingredient is null) return;

        var path = SelectedAsset?.AssetId;
        if (_changes is null || string.IsNullOrEmpty(path) || NeedsProject) return;

        var dropped = _changes.RecordListChange(path!,
            CookingIngredients.IngredientPath(ingredient.IsMain, ingredient.Index),
            ChangeOperation.RemoveEntry, _projects!.Chosen!.Id, assetPath: path);

        CookingNotice = dropped > 0
            ? $"{dropped} other change(s) on this dish were cleared, because removing an ingredient renumbers the rest — please re-check them."
            : string.Empty;
        RefreshShown();
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

    /// <summary>True when this row is a field the project ADDED to the item (not one the file has).</summary>
    public bool IsAdded { get; init; }

    /// <summary>The add record's id, for editing/removing an added field (null otherwise).</summary>
    public Guid? AddRecordId { get; init; }

    /// <summary>True when the shown value differs from the baseline (we changed it).</summary>
    public bool IsChanged => Value != OriginalValue;

    /// <summary>True when the "changed" chip should show (an added field shows "added" instead).</summary>
    public bool ShowChanged => IsChanged && !IsAdded;

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

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(ShowChanged));
    }

    partial void OnOriginalValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(ShowChanged));
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
                        string donorPath, FieldType type, string targetPath,
                        bool isAdded = false, Guid? recordId = null)
    {
        Label = label;
        Unit = unit;
        Meaning = meaning;
        TechnicalName = technicalName;
        DonorPath = donorPath;
        Type = type;
        TargetPath = targetPath;
        _isAdded = isAdded;
        RecordId = recordId;
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

    /// <summary>True when this project has already added the field to the item.</summary>
    [ObservableProperty] private bool _isAdded;

    /// <summary>The add record's id, so it can be removed again (null while not added).</summary>
    public Guid? RecordId { get; private set; }

    /// <summary>True when the "type a value and Add" controls should show.</summary>
    public bool ShowAdd => !IsAdded;

    /// <summary>True when the "added — Remove" controls should show.</summary>
    public bool ShowRemove => IsAdded;

    /// <summary>Mark the field as added by this project (after a successful Add).</summary>
    public void MarkAdded(Guid recordId)
    {
        RecordId = recordId;
        IsAdded = true;
    }

    /// <summary>Mark the field as no longer added (after a Remove): back to a clean "add" row.</summary>
    public void MarkRemoved()
    {
        RecordId = null;
        Draft = "";        // do not leave the old value looking like the field is still set
        IsAdded = false;
    }

    partial void OnIsAddedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAdd));
        OnPropertyChanged(nameof(ShowRemove));
    }

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
