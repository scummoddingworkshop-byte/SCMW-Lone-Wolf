using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>
/// One changeable value on an item: its friendly name, what kind it is, the current text,
/// the original (so we can show "was …"), the section it belongs to, whether it can be
/// changed, and its inline-edit state.
/// </summary>
public partial class ItemValue : ObservableObject
{
    private readonly string _gameValue;

    public ItemValue(string name, string group, string value,
                     ItemValueKind kind = ItemValueKind.Number, bool editable = true,
                     string? technicalName = null)
    {
        _name = name;
        TechnicalName = technicalName ?? name;
        Group = group;
        _value = value;
        _originalValue = value;
        _gameValue = value;
        Kind = kind;
        Editable = editable;
    }

    [ObservableProperty] private string _name;

    /// <summary>
    /// The game's own (technical) field name, used when building the mod. Sample items have no
    /// separate name, so it falls back to the label.
    /// </summary>
    public string TechnicalName { get; }

    /// <summary>The current value, as text.</summary>
    [ObservableProperty] private string _value;

    /// <summary>The value the game itself has, before any project record is applied.</summary>
    public string GameValue => _gameValue;

    /// <summary>The value as it was before the project's first change (its first record's old value).</summary>
    [ObservableProperty] private string _originalValue;

    partial void OnOriginalValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(WasText));
    }

    /// <summary>The section this value belongs to (Inventory, Damage, …).</summary>
    public string Group { get; }

    public ItemValueKind Kind { get; }

    /// <summary>False for values shown only when "Show all values" is on.</summary>
    public bool Editable { get; }

    /// <summary>True when the value differs from the original.</summary>
    public bool IsChanged => Value != OriginalValue;

    /// <summary>Plain "was 10" line shown under a changed value.</summary>
    public string WasText => $"was {OriginalValue}";

    /// <summary>True while the inline editor is open.</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>True when the clickable value should show (editable and not being edited).</summary>
    public bool ShowValueButton => Editable && !IsEditing;

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(ShowValueButton));

    /// <summary>What the player is typing; only committed if valid.</summary>
    [ObservableProperty] private string _draft = "";

    /// <summary>Whether the current draft is a valid value for this kind.</summary>
    public bool IsDraftValid => Kind switch
    {
        // Numbers use the game's format (dot decimal) whatever the machine's culture.
        ItemValueKind.Number => double.TryParse(
            Draft, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _),
        ItemValueKind.Boolean => bool.TryParse(Draft, out _),
        ItemValueKind.Text => !string.IsNullOrWhiteSpace(Draft),
        _ => false,
    };

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(IsChanged));

    partial void OnDraftChanged(string value) => OnPropertyChanged(nameof(IsDraftValid));
}
