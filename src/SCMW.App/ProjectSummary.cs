using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SCMW.Core.Projects;

namespace SCMW.App;

/// <summary>
/// One project as shown on a screen: what it is called, a short description, when it was last
/// changed, an optional picture, whether it is the current project, and whether it is asking
/// to be deleted. Built from an engine <see cref="ProjectRecord"/>.
/// </summary>
public partial class ProjectSummary : ObservableObject
{
    public ProjectSummary(string name, string description, string when, IImage? picture = null)
    {
        _name = name;
        _description = description;
        _when = when;
        Picture = picture;
    }

    /// <summary>The engine's project id.</summary>
    public string Id { get; init; } = "";

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _when;

    /// <summary>Optional project picture; null shows the themed default.</summary>
    public IImage? Picture { get; }

    /// <summary>True for the one project currently open.</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>How many changes this project holds — its own list, never another project's.</summary>
    public int ChangeCount { get; init; }

    /// <summary>Plain words for the count: "No changes yet", "1 change", "3 changes".</summary>
    public string ChangeCountText => ChangeCount switch
    {
        0 => "No changes yet",
        1 => "1 change",
        _ => $"{ChangeCount} changes",
    };

    /// <summary>True when this project has at least one change.</summary>
    public bool HasChanges => ChangeCount > 0;

    /// <summary>True while this project's inline delete confirmation is showing.</summary>
    [ObservableProperty] private bool _isConfirmingDelete;

    /// <summary>The version this project builds with, remembered in its own project file.</summary>
    [ObservableProperty] private string _modVersion = SCMW.Core.Projects.ProjectService.DefaultModVersion;

    /// <summary>True when the card should show its Build controls (the screen is wired to a build).</summary>
    [ObservableProperty] private bool _showBuild;

    /// <summary>True when this card's Build button can start a build right now.</summary>
    [ObservableProperty] private bool _canBuild;

    /// <summary>True when this project has a mod in SCUM, so its switch can do something.</summary>
    [ObservableProperty] private bool _hasMod;

    /// <summary>Whether this project's mod is switched on in SCUM. Flipping it asks the app to
    /// rename the mod's file (see <see cref="ModToggleRequested"/>).</summary>
    [ObservableProperty] private bool _modOn;

    /// <summary>Set by the screen: asks the app to switch this project's mod on or off. The card
    /// itself never touches files.</summary>
    public Action<ProjectSummary, bool>? ModToggleRequested { get; set; }

    /// <summary>The plain words on the switch, so a greyed one explains itself.</summary>
    public string ModToolTip => !HasMod ? "Build this mod first"
        : ModOn ? "On — SCUM loads this mod" : "Off — SCUM skips this mod";

    private bool _settingModState;

    partial void OnHasModChanged(bool value) => OnPropertyChanged(nameof(ModToolTip));

    partial void OnModOnChanged(bool value)
    {
        OnPropertyChanged(nameof(ModToolTip));

        // A state the screen sets is not the owner flipping the switch.
        if (_settingModState) return;
        ModToggleRequested?.Invoke(this, value);
    }

    /// <summary>Shows what the app says about this project's mod. Never asks for a switch.</summary>
    internal void SetModState(bool hasMod, bool on)
    {
        _settingModState = true;
        try
        {
            HasMod = hasMod;
            ModOn = on;
        }
        finally
        {
            _settingModState = false;
        }
    }

    /// <summary>Plain reason the Build button is off ("Nothing to build yet").</summary>
    public string BuildHint => !ShowBuild ? ""
        : !HasChanges ? "Nothing to build yet"
        : CanBuild ? ""
        : "A build is already running";

    partial void OnCanBuildChanged(bool value) => OnPropertyChanged(nameof(BuildHint));

    partial void OnShowBuildChanged(bool value) => OnPropertyChanged(nameof(BuildHint));

    /// <summary>Builds a screen row from a project on disk.</summary>
    public static ProjectSummary From(ProjectRecord record, bool isCurrent, int changeCount = 0) =>
        new(record.Name, record.Description, Describe(record.ModifiedUtc), LoadPicture(record.IconPath))
        {
            Id = record.Id,
            IsCurrent = isCurrent,
            ChangeCount = changeCount,
            ModVersion = record.ModVersion,
        };

    /// <summary>Plain "2 hours ago" text.</summary>
    public static string Describe(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;

        if (age.TotalSeconds < 90) return "just now";
        if (age.TotalMinutes < 60) return Plural((int)age.TotalMinutes, "minute") + " ago";
        if (age.TotalHours < 24) return Plural((int)age.TotalHours, "hour") + " ago";
        return Plural((int)age.TotalDays, "day") + " ago";
    }

    private static string Plural(int count, string word) =>
        count == 1 ? $"1 {word}" : $"{count} {word}s";

    /// <summary>Loads a project picture from a file, tolerating a missing/unreadable one (null).</summary>
    internal static IImage? LoadPicture(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath)) return null;

        try
        {
            return new Bitmap(iconPath);
        }
        catch
        {
            return null;   // an unreadable picture is never fatal — show the default
        }
    }
}
