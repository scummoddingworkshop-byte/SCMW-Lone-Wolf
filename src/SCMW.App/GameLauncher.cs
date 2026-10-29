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

    /// <summary>Start SCUM's own program with the mod-load flags (Steam running in the background).</summary>
    Task<LaunchResult> LaunchModdedAsync();

    /// <summary>Start SCUM's own program as a plain vanilla game.</summary>
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
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
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
/// Starts SCUM's own program (<c>SCUM.exe</c>) from the configured game path, after making sure
/// Steam is running and signed in. <b>Modded</b> passes <c>-nobattleye -fileopenlog</c> (both are
/// needed: BattlEye off, and the file-open log without which SCUM reports a file-integrity error);
/// <b>unmodded</b> starts a plain game. Switching the mods themselves on/off is done by the mods
/// service before this is called.
/// </summary>
public sealed class ScumLauncher : IGameLauncher
{
    /// <summary>SCUM's Steam application id (used for a clean launch through Steam).</summary>
    public const string ScumSteamAppId = "513710";

    private const string ModdedArguments = "-nobattleye -fileopenlog";
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
        return await StartAsync(ModdedArguments, viaSteam: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a clean game through Steam so BattlEye runs (servers need it). The caller must have
    /// switched UE4SS and the mods off first; BattlEye blocks UE4SS's proxy otherwise.
    /// </summary>
    public async Task<LaunchResult> LaunchUnmoddedAsync()
    {
        if (!IsAvailable) return new LaunchResult(false, MissingExe);
        return await StartAsync(null, viaSteam: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes sure Steam is running and signed in, then starts the game. <b>Modded</b> starts
    /// <c>SCUM.exe</c> directly with BattlEye off (going through Steam's <c>-applaunch</c> hands the
    /// launch to <c>SCUM_Launcher.exe</c>, which switches BattlEye on; BattlEye blocks UE4SS's
    /// <c>dwmapi.dll</c> and the game dies with <c>0xc000009a</c>). <b>Clean</b> goes through Steam on
    /// purpose, so BattlEye does run.
    /// </summary>
    private async Task<LaunchResult> StartAsync(string? args, bool viaSteam)
    {
        try
        {
            var steam = _process.FindSteamExe();

            if (viaSteam && steam is null)
            {
                return new LaunchResult(false,
                    "Steam wasn't found, so BattlEye can't start. Set up Steam, then try again.");
            }

            if (steam is not null)
            {
                if (!_process.SteamRunning)
                {
                    _process.Start(steam, null);                       // start Steam
                }

                // Wait even when Steam is already open: it may still be on the sign-in screen,
                // updating, or restarting, and SCUM needs a signed-in Steam.
                var ready = await _process.WaitForSteamAsync(SteamStartupWait).ConfigureAwait(false);
                if (!ready)
                {
                    return new LaunchResult(false, "Steam isn't ready yet — sign in, then try again.");
                }
            }

            if (viaSteam && steam is not null)
            {
                _process.Start(steam, $"-applaunch {ScumSteamAppId}");  // clean: let BattlEye start
            }
            else
            {
                _process.Start(_exePath, args);                        // modded: the game itself
            }

            return new LaunchResult(true, null);
        }
        catch (Exception)
        {
            return new LaunchResult(false, "SCUM could not be started.");
        }
    }
}
