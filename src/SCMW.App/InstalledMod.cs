using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>One mod installed in SCUM: what it is, where it came from, and whether it is on.</summary>
public partial class InstalledMod : ObservableObject
{
    public InstalledMod(string name, bool external, bool isOn = true, string? fileName = null,
                        bool managed = true, string location = "~mods")
    {
        Name = name;
        IsExternal = external;
        _isOn = isOn;
        FileName = fileName;
        Managed = managed;
        Location = location;
    }

    public string Name { get; }

    /// <summary>The on-disk file name (e.g. "B.pak.disabled"), or null for a sample row.</summary>
    public string? FileName { get; }

    /// <summary>True for a mod that came from elsewhere (not built here).</summary>
    public bool IsExternal { get; }

    /// <summary>True when SCMW may switch or remove this mod (it sits in our own ~mods folder).</summary>
    public bool Managed { get; }

    /// <summary>The folder it lives in (~mods, Mods, LogicMods) — another tool manages the last two.</summary>
    public string Location { get; }

    /// <summary>The badge shown beside the name.</summary>
    public string Badge => !Managed ? Location : (IsExternal ? "External" : "SCMW");

    /// <summary>Whether the game should load it. Off never deletes anything.</summary>
    [ObservableProperty] private bool _isOn;

    /// <summary>True while this row's inline remove confirmation is showing.</summary>
    [ObservableProperty] private bool _isConfirmingRemove;
}
