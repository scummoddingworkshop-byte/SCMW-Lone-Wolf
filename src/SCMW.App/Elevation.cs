using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace SCMW.App;

/// <summary>
/// Whether SCMW is running with administrator rights, and how to restart with them. Behind an
/// interface so the screens can be tested without touching the real Windows token or relaunching.
/// </summary>
public interface IElevation
{
    /// <summary>True when this copy of SCMW already has administrator rights.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Opens a new SCMW with administrator rights (Windows shows its Yes/No prompt). Returns false
    /// when the player says No, so the caller can leave the current window exactly as it is.
    /// </summary>
    bool RestartAsAdmin();
}

/// <summary>The real Windows half: reads the process token and relaunches elevated.</summary>
public sealed class WindowsElevation : IElevation
{
    public static WindowsElevation Default { get; } = new();

    /// <summary>
    /// Called after a successful relaunch, to close this window cleanly (so its size and position
    /// are remembered). Set once by the app.
    /// </summary>
    public Action? Shutdown { get; set; }

    public bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;   // no token we can read — treat as "not elevated"
            }
        }
    }

    public bool RestartAsAdmin()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception)
        {
            return false;   // the player declined the Windows prompt
        }

        Shutdown?.Invoke();
        return true;
    }
}
