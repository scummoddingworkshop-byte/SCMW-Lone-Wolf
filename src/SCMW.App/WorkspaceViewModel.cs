// src/SCMW.App/WorkspaceViewModel.cs
// The adapting Workspace: opens one file of any kind and turns its real fields into rows,
// explained from the field library (label, unit, risk dot, plain meaning).
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
