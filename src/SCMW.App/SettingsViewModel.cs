using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCMW.App.Setup;
using SCMW.Core.Content;
using SCMW.Core.Setup;
using SCMW.Core.Settings;
using SCMW.Core.Workspace;

namespace SCMW.App;

/// <summary>
/// Settings' brain. Everything here is real: values come from <see cref="ISettingsStore"/>,
/// changes are written straight back, and detection uses the engine's <see cref="ScumLocator"/>.
/// It knows nothing about pixels.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IScumSources _scumSources;
    private readonly IContentSource? _content;
    private readonly WorkspacePaths? _workspace;
    private readonly IFolderOpener _folders;
    private readonly ILinkOpener _links;
    private readonly INavigation _nav;
    private bool _loading;

    /// <summary>Raised when the game-detection status changes, so the header chip can follow it.</summary>
    public event Action? GameChanged;

    /// <summary>Raised when SCUM's folder/key are set up well enough to use — so the app can come
    /// alive without a restart (load the real items, wake Build, Mods and Launch).</summary>
    public event Action? GameConfigured;

    /// <summary>Raised after the saved game data is cleared, so the app re-reads SCUM.</summary>
    public event Action? CacheCleared;

    public SettingsViewModel(ISettingsStore? store = null, IScumSources? scumSources = null,
                             IContentSource? contentSource = null, WorkspacePaths? workspace = null,
                             IFolderOpener? folderOpener = null, INavigation? nav = null,
                             ILinkOpener? linkOpener = null)
    {
        _store = store ?? new InMemorySettingsStore();
        _scumSources = scumSources ?? WindowsScumSources.Default;
        _content = contentSource;
        _workspace = workspace;
        _folders = folderOpener ?? new SystemFolderOpener();
        _nav = nav ?? NavigationHost.Shared;
        _links = linkOpener ?? new SystemLinkOpener();

        var settings = _store.Load();

        _loading = true;
        _gamePath = settings.GamePath ?? "";
        _workspacePath = settings.WorkspacePath ?? "";
        _unlockKey = settings.AesKey ?? "";
        _buildId = settings.BuildId ?? "";
        _activeProjectId = settings.ActiveProjectId ?? "";
        _loading = false;

        RefreshGameStatus();
        RefreshStorage();
        RefreshModsPath();
        _logPath = _workspace?.Logs ?? "";
    }

    // ── Stored settings ─────────────────────────────────────────────────────
    [ObservableProperty] private string _gamePath;
    [ObservableProperty] private string _workspacePath;
    [ObservableProperty] private string _unlockKey;
    [ObservableProperty] private string _buildId;
    [ObservableProperty] private string _activeProjectId;

    partial void OnGamePathChanged(string value)
    {
        GameHint = "";              // a manually-changed path retires any old "detect" hint
        GameFolderNote = "";
        RefreshGameStatus();
        RefreshModsPath();
        Persist();
    }

    partial void OnWorkspacePathChanged(string value) => Persist();

    partial void OnUnlockKeyChanged(string value)
    {
        SetKeyStatus("");            // an old "works" must not linger after the key is edited
        Persist();
    }

    partial void OnBuildIdChanged(string value) => Persist();

    /// <summary>True when the settings are really being written to disk.</summary>
    private bool Persistent => _store is not InMemorySettingsStore;

    /// <summary>Plain warning shown on the screen when settings cannot be saved.</summary>
    public string StorageNote => Persistent
        ? ""
        : "This session only — your settings can't be saved right now.";

    private void Persist()
    {
        if (_loading) return;

        _store.Save(new AppSettings(
            WorkspacePath: string.IsNullOrWhiteSpace(WorkspacePath) ? null : WorkspacePath,
            GamePath: string.IsNullOrWhiteSpace(GamePath) ? null : GamePath,
            BuildId: string.IsNullOrWhiteSpace(BuildId) ? null : BuildId,
            AesKey: string.IsNullOrWhiteSpace(UnlockKey) ? null : UnlockKey,
            ActiveProjectId: string.IsNullOrWhiteSpace(ActiveProjectId) ? null : ActiveProjectId));
    }

    // ── Game ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _gameStatus = "SCUM not found";
    [ObservableProperty] private bool _gameFound;

    /// <summary>The engine's plain hint when detection found nothing (empty when all is well).</summary>
    [ObservableProperty] private string _gameHint = "";

    /// <summary>Plain answer when "Open folder" can't open the game folder.</summary>
    [ObservableProperty] private string _gameFolderNote = "";

    /// <summary>The folder SCUM loads mods from, shown so it can be seen and opened.</summary>
    [ObservableProperty] private string _modsPath = "";

    /// <summary>Plain answer when "Open folder" can't open the mods folder.</summary>
    [ObservableProperty] private string _modsNote = "";

    /// <summary>
    /// Reports what is actually true about the folder: nothing set, set but not SCUM, or a
    /// verified install. Never claims a detection it has not checked.
    /// </summary>
    private void RefreshGameStatus()
    {
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            GameFound = false;
            GameStatus = "SCUM not found";
        }
        else
        {
            GameFound = ScumLocator.VerifyInstalledDir(GamePath, _scumSources);
            GameStatus = GameFound
                ? "SCUM detected"
                : "SCUM folder set, but it doesn't look like SCUM";
        }

        GameChanged?.Invoke();
    }

    [RelayCommand]
    private void DetectGame()
    {
        var result = ScumLocator.Locate(_scumSources);

        if (result.Found && result.Path is not null)
        {
            GamePath = result.Path;              // also refreshes the status and saves
            BuildId = result.BuildId;
            GameHint = "";
            RefreshGameStatus();
            GameConfigured?.Invoke();            // found it — the app can come alive now
            return;
        }

        // Steam could not see it. Show the engine's own hint, and never contradict a folder
        // that is already verified.
        RefreshGameStatus();
        GameHint = result.Hint;
    }

    /// <summary>Opens the SCUM folder in the file browser, so the player can see it for real.</summary>
    [RelayCommand]
    private void OpenGameFolder()
    {
        GameFolderNote = _folders.Open(GamePath)
            ? ""
            : "Set your SCUM folder above first.";
    }

    /// <summary>Keeps the shown mods path in step with the game folder (same path SCUM loads from).</summary>
    private void RefreshModsPath()
    {
        ModsPath = string.IsNullOrWhiteSpace(GamePath)
            ? ""
            : SCMW.Backend.Build.BuildNames.ModsFolder(GamePath);
        ModsNote = "";
    }

    /// <summary>Opens SCUM's mods folder (where SCMW — Lone Wolf installs, and SCUM loads, your mods).</summary>
    [RelayCommand]
    private void OpenModsFolder()
    {
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            ModsNote = "Set your SCUM folder above first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ModsPath)) RefreshModsPath();

        ModsNote = _folders.Open(ModsPath)
            ? ""
            : "Your mods folder isn't there yet — it appears once a mod is installed.";
    }

    // ── Unlock key ──────────────────────────────────────────────────────────
    // The key saves itself as it is typed (OnUnlockKeyChanged → Persist), so there is no Save button.
    // This quiet line is only ever used for the answer to "Test".
    [ObservableProperty] private string _keyStatus = "";

    /// <summary>True when the last test succeeded — the line shows green.</summary>
    [ObservableProperty] private bool _keyTestGood;

    /// <summary>True when the last test failed — the line shows red.</summary>
    [ObservableProperty] private bool _keyTestBad;

    /// <summary>Quiet answer for the "Where do I get this?" link.</summary>
    [ObservableProperty] private string _keyHelpNote = "";

    /// <summary>Plain answer when a key-source link can't be opened.</summary>
    [ObservableProperty] private string _keyLinkNote = "";

    private void SetKeyStatus(string message, bool good = false, bool bad = false)
    {
        KeyStatus = message;
        KeyTestGood = good;
        KeyTestBad = bad;
    }

    [RelayCommand]
    private async Task TestKey()
    {
        if (string.IsNullOrWhiteSpace(UnlockKey))
        {
            SetKeyStatus("Enter a key first, then test it.");
            return;
        }

        if (_content is null || string.IsNullOrWhiteSpace(GamePath))
        {
            SetKeyStatus("Set your SCUM folder in Settings first, then test the key.");
            return;
        }

        SetKeyStatus("Testing the key…");

        // Run off the UI thread (reading SCUM's paks is slow); the message comes back on the UI thread.
        var path = GamePath;
        var key = UnlockKey;
        var buildId = BuildId;
        var (message, good) = await Task.Run(async () =>
        {
            try
            {
                var paks = Path.Combine(path, "SCUM", "Content", "Paks");
                _content.Configure(paks, key, buildId);
                var status = await _content.ScanAsync();
                return status.State == ContentSourceState.Ready
                    ? ("That key works — SCUM's files can be read.", true)
                    : ("That key didn't work. " + status.Message, false);
            }
            catch
            {
                return ("That key couldn't be tested. Check the game folder, then try again.", false);
            }
        });

        SetKeyStatus(message, good: good, bad: !good);
        if (good) GameConfigured?.Invoke();      // the key works — the app can come alive now
    }

    [RelayCommand]
    private void WhereKey() => KeyHelpNote =
        "1.  Open the key list below — a public page of Unreal Engine keys. No download, no sign-up.\n" +
        "2.  Press Ctrl+F, search for SCUM, and copy the long key it lists.\n" +
        "3.  Paste it above and press Test.\n\n" +
        "It's the same key for everyone, and the same key other SCUM tools use. If Test fails after a " +
        "SCUM update, the list may be out of date — the FModel website links to their Discord, where " +
        "the current key is posted.";

    /// <summary>Opens the public key list in the browser.</summary>
    [RelayCommand]
    private void OpenKeyList() => KeyLinkNote = _links.Open(KeySources.PublicList)
        ? ""
        : "The key list couldn't be opened in your browser.";

    /// <summary>Opens the FModel website (its Discord has the current key when the list is out of date).</summary>
    [RelayCommand]
    private void OpenKeyFallback() => KeyLinkNote = _links.Open(KeySources.Fallback)
        ? ""
        : "The FModel website couldn't be opened in your browser.";

    // ── Workspace ───────────────────────────────────────────────────────────
    /// <summary>How much the saved game data (item list + pictures) takes up.</summary>
    [ObservableProperty] private string _storageUsed = "Not tracked yet";
    [ObservableProperty] private string _cacheNote = "";
    [ObservableProperty] private bool _cacheNoteGood;
    [ObservableProperty] private string _workspaceNote = "";

    /// <summary>True while the app is re-reading SCUM after a rebuild (so the button rests).</summary>
    [ObservableProperty] private bool _isRefreshing;

    /// <summary>Called when the app has finished re-reading SCUM; says plainly how it went.</summary>
    public void RefreshFinished(string message, bool good = false)
    {
        IsRefreshing = false;
        CacheNote = message;
        CacheNoteGood = good;
        RefreshStorage();          // the re-read has written a fresh cache — measure it now
    }

    /// <summary>The saved game data lives here: the item list (assets.db) and the pictures.</summary>
    private string? CacheFolder =>
        string.IsNullOrWhiteSpace(_workspace?.Cache) || !Directory.Exists(_workspace.Cache)
            ? null
            : _workspace!.Cache;

    /// <summary>Sums the cache folder and shows it plainly ("12.4 MB"), or says nothing is measured.</summary>
    private void RefreshStorage()
    {
        var cache = CacheFolder;
        if (cache is null)
        {
            StorageUsed = "Not tracked yet";
            return;
        }

        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* a file vanished mid-count — ignore it */ }
            }

            StorageUsed = DescribeSize(total);
        }
        catch
        {
            StorageUsed = "Not tracked yet";
        }
    }

    /// <summary>Bytes in plain words ("Empty", "512 bytes", "12.4 MB", "1.1 GB").</summary>
    private static string DescribeSize(long bytes)
    {
        if (bytes <= 0) return "Empty";

        string[] units = { "bytes", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }

        return unit == 0 ? $"{bytes} bytes" : $"{value:0.#} {units[unit]}";
    }

    /// <summary>
    /// Throws away the saved item list and pictures so they are read fresh from SCUM. Projects and
    /// settings are untouched. Then tells the app to re-read, so the change takes effect now.
    /// </summary>
    [RelayCommand]
    private async Task ClearCache()
    {
        var cache = CacheFolder;
        if (cache is null)
        {
            CacheNote = "There's nothing saved to clear yet.";
            return;
        }

        try
        {
            // Pictures first, through the engine, so its own bookkeeping stays in step.
            if (_content is not null)
            {
                try { await _content.ClearIconsAsync(); }
                catch { /* fall through to the plain folder cleanup below */ }
            }

            // The item list, and any SQLite side files.
            foreach (var name in new[] { "assets.db", "assets.db-wal", "assets.db-shm" })
            {
                var file = Path.Combine(cache, name);
                if (File.Exists(file)) File.Delete(file);
            }

            // Any picture files the engine did not look after.
            var icons = Path.Combine(cache, "icons");
            if (Directory.Exists(icons)) Directory.Delete(icons, recursive: true);

            RefreshStorage();
            CacheNote = "Saved game data cleared. Re-reading your items from SCUM…";
            IsRefreshing = true;
            CacheCleared?.Invoke();
        }
        catch
        {
            CacheNote = "The saved game data couldn't be cleared. Close the app and try again.";
        }
    }

    [RelayCommand]
    private void OpenWorkspace()
    {
        var folder = _workspace?.Root;
        if (string.IsNullOrWhiteSpace(folder)) folder = WorkspacePath;

        WorkspaceNote = _folders.Open(folder)
            ? ""
            : "There's no folder to open yet — set one above.";
    }

    /// <summary>Plain answer when the settings folder can't be opened.</summary>
    [ObservableProperty] private string _settingsFolderNote = "";

    /// <summary>Opens the folder that holds settings.json (and the unlock key).</summary>
    [RelayCommand]
    private void OpenSettingsFolder()
    {
        SettingsFolderNote = _folders.Open(AppPaths.Default.SettingsFolder)
            ? ""
            : "The settings folder couldn't be opened.";
    }

    // ── About ───────────────────────────────────────────────────────────────
    public string Version => AppInfo.Version;

    /// <summary>The About card's Help button opens the Help screen.</summary>
    [RelayCommand]
    private void OpenHelp() => _nav.GoTo(ScreenKey.Help);

    // ── Support ─────────────────────────────────────────────────────────────
    /// <summary>The folder the app writes its log file to (shown so it can be found and shared).</summary>
    [ObservableProperty] private string _logPath = "";

    /// <summary>Plain answer when the log folder can't be opened.</summary>
    [ObservableProperty] private string _logNote = "";

    /// <summary>Opens the folder the app's log lives in, for when something goes wrong.</summary>
    [RelayCommand]
    private void OpenLogsFolder()
    {
        LogNote = _folders.Open(_workspace?.Logs)
            ? ""
            : "There's no log folder to open yet.";
    }

    /// <summary>Plain answer when the licence folder can't be opened.</summary>
    [ObservableProperty] private string _licenceNote = "";

    /// <summary>True when there is a support page to link to (there always is, but no dead button).</summary>
    public bool ShowSupport => !string.IsNullOrWhiteSpace(AppInfo.SupportUrl);

    /// <summary>A plain answer when the support page can't be opened.</summary>
    [ObservableProperty] private string _supportNote = "";

    /// <summary>Opens the project's Ko-fi page in the browser — a quiet, optional thank-you.</summary>
    [RelayCommand]
    private void OpenSupport() => SupportNote = _links.Open(AppInfo.SupportUrl)
        ? ""
        : "The support page couldn't be opened.";

    /// <summary>Opens the folder beside the app that holds THIRD-PARTY-NOTICES.md and the licence texts.</summary>
    [RelayCommand]
    private void OpenLicenceFiles()
    {
        LicenceNote = _folders.Open(AppContext.BaseDirectory)
            ? ""
            : "The licence files couldn't be opened.";
    }
}
