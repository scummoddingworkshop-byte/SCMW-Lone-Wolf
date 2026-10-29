using System;
using System.IO;

namespace SCMW.App;

/// <summary>Whether a switch worked; one plain reason if not.</summary>
public sealed record SwitchResult(bool Ok, string? Error);

/// <summary>Turns the UE4SS mod loader off and on again (for clean online play).</summary>
public interface IUe4ssSwitch
{
    /// <summary>True when the mod loader is currently switched off.</summary>
    bool IsOff { get; }

    /// <summary>Move UE4SS's proxy aside so the game runs without the mod loader.</summary>
    SwitchResult TurnOff();

    /// <summary>Put the proxy back so the mod loader works again.</summary>
    SwitchResult TurnOn();
}

/// <summary>
/// Switches UE4SS off by moving its <c>dwmapi.dll</c> proxy in SCUM's Win64 folder. UE4SS gets into
/// the game through that one file, so moving it is all it takes — and moving it back restores
/// everything. Nothing else is touched, and no file is ever deleted.
/// </summary>
public sealed class Ue4ssSwitch : IUe4ssSwitch
{
    public const string ProxyName = "dwmapi.dll";
    public const string OffSuffix = ".scmw-off";

    private const string Busy = "Close SCUM, then try again.";

    private readonly bool _available;
    private readonly string _proxy = "";
    private readonly string _off = "";

    /// <param name="gameRoot">The game path from Settings (the folder that holds <c>SCUM\Content\Paks</c>).</param>
    public Ue4ssSwitch(string? gameRoot)
    {
        var root = (gameRoot ?? "").TrimEnd('\\', '/');
        _available = root.Length > 0;
        if (!_available) return;

        var win64 = Path.Combine(root, "SCUM", "Binaries", "Win64");
        _proxy = Path.Combine(win64, ProxyName);
        _off = _proxy + OffSuffix;
    }

    public bool IsOff => _available && File.Exists(_off) && !File.Exists(_proxy);

    public SwitchResult TurnOff()
    {
        if (!_available || !File.Exists(_proxy)) return new SwitchResult(true, null);   // not installed

        try
        {
            File.Move(_proxy, _off, overwrite: true);
            return new SwitchResult(true, null);
        }
        catch (Exception)
        {
            return new SwitchResult(false, Busy);
        }
    }

    public SwitchResult TurnOn()
    {
        if (!_available || !File.Exists(_off)) return new SwitchResult(true, null);    // nothing to restore

        // Both present means an earlier restore was interrupted: the live file wins.
        if (File.Exists(_proxy))
        {
            try { File.Delete(_off); } catch { /* harmless leftover */ }
            return new SwitchResult(true, null);
        }

        try
        {
            File.Move(_off, _proxy);
            return new SwitchResult(true, null);
        }
        catch (Exception)
        {
            return new SwitchResult(false, Busy);
        }
    }
}
