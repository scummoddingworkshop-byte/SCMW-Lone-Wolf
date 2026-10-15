using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCMW.App;

/// <summary>
/// The shared, in-memory list of mods installed in SCUM. A successful Build installs into
/// it; the Mods screen reads and edits it. It is the single source of truth for
/// "what is installed".
/// </summary>
public partial class ModList : ObservableObject
{
    private readonly ObservableCollection<InstalledMod> _mods = new();

    public IReadOnlyList<InstalledMod> Mods => _mods;

    public int InstalledCount => _mods.Count;
    public int OnCount => _mods.Count(m => m.IsOn);

    /// <summary>Quiet line: "3 mods installed · 2 switched on."</summary>
    public string Summary =>
        $"{InstalledCount} {(InstalledCount == 1 ? "mod" : "mods")} installed · " +
        $"{OnCount} switched on.";

    /// <summary>Installs (or refreshes) a mod built here.</summary>
    public InstalledMod Install(string name)
    {
        var existing = _mods.FirstOrDefault(m => !m.IsExternal && m.Name == name);
        if (existing is not null)
        {
            existing.IsOn = true;
            RaiseChanged();
            return existing;
        }

        return Track(new InstalledMod(name, external: false));
    }

    /// <summary>Adds a mod that came from elsewhere.</summary>
    public InstalledMod Add(string name) => Track(new InstalledMod(name, external: true));

    /// <summary>
    /// Puts a mod built here into the list, switched OFF — how a fresh build arrives. The player
    /// switches it on themselves, so nothing new affects the game without them choosing it.
    /// </summary>
    public InstalledMod Place(string name)
    {
        var existing = _mods.FirstOrDefault(m => !m.IsExternal && m.Name == name);
        if (existing is not null)
        {
            existing.IsOn = false;
            RaiseChanged();
            return existing;
        }

        return Track(new InstalledMod(name, external: false) { IsOn = false });
    }

    public void Remove(InstalledMod mod)
    {
        if (!_mods.Contains(mod)) return;

        mod.IsConfirmingRemove = false;
        mod.PropertyChanged -= OnModChanged;
        _mods.Remove(mod);
        RaiseChanged();
    }

    private InstalledMod Track(InstalledMod mod)
    {
        mod.PropertyChanged += OnModChanged;
        _mods.Add(mod);
        RaiseChanged();
        return mod;
    }

    private void OnModChanged(object? sender, PropertyChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(Mods));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(OnCount));
        OnPropertyChanged(nameof(Summary));
    }
}
