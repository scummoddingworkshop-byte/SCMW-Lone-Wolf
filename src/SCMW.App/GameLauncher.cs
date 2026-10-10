using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using SCMW.App.Setup;

namespace SCMW.App;

/// <summary>Whether a launch worked; one plain reason if not.</summary>
public sealed record LaunchResult(bool Ok, string? Error);

/// <summary>Starts SCUM, with or without the player's mods. Abstracted so screens and tests never
/// touch the real game or Steam. Switching mods on/off is the mods service's job.</summary>
public interface IGameLauncher
{
    /// <summary>True when SCUM's program was found under the configured game path.</summary>
    bool IsAvailable { get; }

    /// <summary>Start SCUM through Steam with the mod-load flags.</summary>
    Task<LaunchResult> LaunchModdedAsync();

    /// <summary>Start SCUM as a plain vanilla game.</summary>
    Task<LaunchResult> LaunchUnmoddedAsync();
}

/// <summary>Runs processes and finds/starts Steam. Abstracted so tests start nothing.</summary>
public interface IProcessLauncher
{
    bool SteamRunning { get; }
    string? FindSteamExe();
    void Start(string exe, string? args);
    Task<bool> WaitForSteamAsync(TimeSpan timeout);
}

/// <summary>The real one: real processes, Steam found from the registry.</summary>
public sealed class SystemProcessLauncher : IProcessLauncher
{
    public bool SteamRunning => Process.GetProcessesByName("steam").Length > 0;

    public string? FindSteamExe()
    {
        foreach (var root in WindowsScumSources.Default.SteamRoots())
        {
            var exe = Path.Combine(root, "steam.exe");
            if (File.Exists(exe)) return exe;
        }

        return null;
    }

    public void Start(string exe, string? args) =>
        Process.Start(new ProcessStartInfo(exe)
        {
            Arguments = args ?? "",
            UseShellExecute = true,
        });

    public async Task<bool> WaitForSteamAsync(TimeSpan timeout)
    {
        // Steam merely being *open* is not enough: it must be signed in and ready, or the game
        // launch handshake fails (the "SteamWebhelper is not responding" hang after a cold start).
        // Wait for Steam's own ActiveUser marker as well as its process.
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (SteamRunning && SteamReady()) return true;
            await Task.Delay(500).ConfigureAwait(false);
        }

        return SteamRunning && SteamReady();
    }

    private static bool SteamReady()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            return IsSignedIn(key?.GetValue("ActiveUser"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when Steam's ActiveUser marker says a user is signed in (a DWORD greater than 0).</summary>
    internal static bool IsSignedIn(object? activeUser) => activeUser switch
    {
        int i => i > 0,
        long l => l > 0,
        _ => false,
    };
}

/// <summary>
/// Starts SCUM from the configured game path, through Steam, ensuring Steam is running first.
/// <b>Modded</b> passes <c>-nobattleeye -fileopenlog</c> (the flags the proven versions needed for
/// mods to load); <b>unmodded</b> starts a plain game. Switching the mods themselves on/off is done
/// by the mods service before this is called.
/// </summary>
public sealed class ScumLauncher : IGameLauncher
{
    /// <summary>SCUM's Steam application id (used to launch through Steam).</summary>
    public const string ScumSteamAppId = "513710";

    private const string ModdedArguments = "-nobattleeye -fileopenlog";
    private static readonly TimeSpan SteamStartupWait = TimeSpan.FromSeconds(45);
    private const string MissingExe =
        "SCUM's program (SCUM.exe) wasn't found — check the game path in Settings.";

    private readonly string _exePath;
    private readonly IProcessLauncher _process;

    /// <param name="gameRoot">The game path from Settings (the folder that holds <c>SCUM\Content\Paks</c>).</param>
    /// <param name="processLauncher">Starts programs; injected so tests never launch anything.</param>
    public ScumLauncher(string? gameRoot, IProcessLauncher? processLauncher = null)
    {
        var root = (gameRoot ?? "").TrimEnd('\\', '/');
        var scum = root.Length == 0 ? "" : Path.Combine(root, "SCUM");
        _exePath = scum.Length == 0 ? "" : Path.Combine(scum, "Binaries", "Win64", "SCUM.exe");
        _process = processLauncher ?? new SystemProcessLauncher();
    }

    public bool IsAvailable => _exePath.Length > 0 && File.Exists(_exePath);

    public async Task<LaunchResult> LaunchModdedAsync()
    {
        if (!IsAvailable) return new LaunchResult(false, MissingExe);
        return await StartAsync(ModdedArguments).ConfigureAwait(false);
    }

    public async Task<LaunchResult> LaunchUnmoddedAsync()
    {
        if (!IsAvailable) return new LaunchResult(false, MissingExe);
        return await StartAsync(null).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts SCUM through Steam (making sure Steam is running first). If Steam can't be found,
    /// falls back to starting the game's own program so launching still works.
    /// </summary>
    private async Task<LaunchResult> StartAsync(string? args)
    {
        try
        {
            var steam = _process.FindSteamExe();
            if (steam is not null)
            {
                if (!_process.SteamRunning)
                {
                    _process.Start(steam, null);                       // start Steam
                    await _process.WaitForSteamAsync(SteamStartupWait).ConfigureAwait(false);
                }

                var launchArgs = args is null
                    ? $"-applaunch {ScumSteamAppId}"
                    : $"-applaunch {ScumSteamAppId} {args}";
                _process.Start(steam, launchArgs);                     // launch SCUM via Steam
            }
            else
            {
                _process.Start(_exePath, args);                        // no Steam found: direct
            }

            return new LaunchResult(true, null);
        }
        catch (Exception)
        {
            return new LaunchResult(false, "SCUM could not be started.");
        }
    }
}
