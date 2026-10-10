using Avalonia.Headless.XUnit;
using SCMW.Core.Setup;
using SCMW.Core.Settings;

namespace SCMW.App.Tests;

public class SettingsViewModelTests
{
    [AvaloniaFact]
    public void Detecting_the_game_announces_it_is_configured()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FoundScumSources());
        var fired = 0; vm.GameConfigured += () => fired++;

        vm.DetectGameCommand.Execute(null);

        Assert.True(vm.GameFound);
        Assert.Equal(1, fired);
    }

    [AvaloniaFact]
    public async Task A_working_key_announces_the_game_is_configured()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            new FakeContent(SCMW.Core.Content.ContentSourceState.Ready));
        var fired = 0; vm.GameConfigured += () => fired++;
        vm.GamePath = @"D:\Games\SCUM";
        vm.UnlockKey = "KEY";

        await vm.TestKeyCommand.ExecuteAsync(null);

        Assert.Equal(1, fired);
    }

    /// <summary>A SCUM sources stand-in: no registry, no disk.</summary>
    private sealed class FakeScumSources : IScumSources
    {
        private readonly Dictionary<string, bool> _files = new();

        public FakeScumSources(params (string path, bool present)[] files)
        {
            foreach (var (path, present) in files) _files[path] = present;
        }

        public IReadOnlyList<string> SteamRoots() => Array.Empty<string>();
        public string? ReadAcf(string steamAppsDir, string fileName) => null;
        public bool FileExists(string path) => _files.TryGetValue(path, out var present) && present;
    }

    /// <summary>A SCUM install that Steam detection really finds (for the Detect test).</summary>
    private sealed class FoundScumSources : IScumSources
    {
        public IReadOnlyList<string> SteamRoots() => new[] { @"D:\Steam" };

        public string? ReadAcf(string steamAppsDir, string fileName) =>
            fileName == "appmanifest_513710.acf"
                ? "\"AppState\"\n{\n\t\"installdir\"\t\t\"SCUM\"\n\t\"buildid\"\t\t\"12345\"\n}\n"
                : null;

        public bool FileExists(string path) =>
            path.Replace('\\', '/').EndsWith("SCUM/SCUM/Binaries/Win64/SCUM.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A content-source stand-in that reports a fixed scan state — no game, no disk.</summary>
    private sealed class FakeContent : SCMW.Core.Content.IContentSource
    {
        private readonly SCMW.Core.Content.ContentSourceState _state;
        public FakeContent(SCMW.Core.Content.ContentSourceState state) => _state = state;

        public SCMW.Core.Content.ContentSourceStatus Status =>
            new(_state, _state == SCMW.Core.Content.ContentSourceState.Ready ? "mounted" : "that key didn't work", null);
        public SCMW.Core.Content.ScanInfo? LastScan => null;
        public SCMW.Core.Content.IIconCache Icons => throw new NotSupportedException();
        public IReadOnlyList<string> Categories => Array.Empty<string>();
        public void Configure(string p, string? k, string? b) { }
        public Task<SCMW.Core.Content.ContentSourceStatus> ScanAsync(IProgress<int>? p = null, CancellationToken ct = default)
            => Task.FromResult(Status);
        public Task<IReadOnlyList<SCMW.Core.Content.AssetSummary>> SearchAsync(string q, string? c, int s = 0, int t = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SCMW.Core.Content.AssetSummary>>(Array.Empty<SCMW.Core.Content.AssetSummary>());
        public Task<IReadOnlyList<SCMW.Core.Content.AssetField>> FieldsAsync(string id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SCMW.Core.Content.AssetField>>(Array.Empty<SCMW.Core.Content.AssetField>());
        public Task<SCMW.Core.Content.IconExtractionProgress> ExtractAllIconsAsync(IProgress<SCMW.Core.Content.IconExtractionProgress>? p = null, CancellationToken ct = default)
            => Task.FromResult(new SCMW.Core.Content.IconExtractionProgress(0, 0, 0, 0));
        public Task<SCMW.Core.Content.IconExtractionProgress> ExtractIconsAsync(IEnumerable<string> ids, IProgress<SCMW.Core.Content.IconExtractionProgress>? p = null, CancellationToken ct = default)
            => Task.FromResult(new SCMW.Core.Content.IconExtractionProgress(0, 0, 0, 0));
        public Task ClearIconsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Starts_from_the_stored_settings()
    {
        var store = new InMemorySettingsStore();
        store.Save(new AppSettings(@"C:\Workspace", @"D:\Games\SCUM", AesKey: "KEY-1"));

        var vm = new SettingsViewModel(store, new FakeScumSources());

        Assert.Equal(@"D:\Games\SCUM", vm.GamePath);
        Assert.Equal(@"C:\Workspace", vm.WorkspacePath);
        Assert.Equal("KEY-1", vm.UnlockKey);
    }

    [AvaloniaFact]
    public void Changing_a_setting_writes_it_immediately()
    {
        var store = new InMemorySettingsStore();
        var vm = new SettingsViewModel(store, new FakeScumSources());

        vm.WorkspacePath = @"C:\Somewhere\Else";

        Assert.Equal(@"C:\Somewhere\Else", store.Load().WorkspacePath);
    }

    [AvaloniaFact]
    public void A_missing_folder_is_reported_and_never_claims_a_detection()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());

        Assert.Equal("SCUM not found", vm.GameStatus);

        vm.GamePath = @"D:\Games\SCUM";     // set, but not verified

        Assert.False(vm.GameFound);
        Assert.Contains("doesn't look like SCUM", vm.GameStatus);
    }

    [AvaloniaFact]
    public void A_real_looking_folder_is_reported_as_detected()
    {
        // The locator's marker check builds "<dir>/SCUM/Binaries/Win64/SCUM.exe".
        var exe = @"D:\Games\SCUM/SCUM/Binaries/Win64/SCUM.exe";
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources((exe, true)));

        vm.GamePath = @"D:\Games\SCUM";

        Assert.True(vm.GameFound);
        Assert.Equal("SCUM detected", vm.GameStatus);
    }

    [AvaloniaFact]
    public void A_missing_key_shows_no_nagging_message()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());

        Assert.Empty(vm.KeyStatus);          // no key set: the field's own prompt is enough

        vm.UnlockKey = "ABC-123";

        Assert.Empty(vm.KeyStatus);          // and still quiet once a key is there
    }

    [AvaloniaFact]
    public void Setting_a_key_saves_it_right_away()
    {
        var store = new InMemorySettingsStore();
        var vm = new SettingsViewModel(store, new FakeScumSources());

        vm.UnlockKey = "ABC-123";            // no Save button: it saves as it is typed

        Assert.Equal("ABC-123", store.Load().AesKey);
    }

    [AvaloniaFact]
    public void Testing_an_empty_key_asks_for_one()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());

        vm.TestKeyCommand.Execute(null);

        Assert.Contains("Enter a key", vm.KeyStatus);
    }

    [AvaloniaFact]
    public void Testing_a_key_without_a_game_folder_says_to_set_it()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources())
        {
            UnlockKey = "ABC-123",
        };

        vm.TestKeyCommand.Execute(null);

        Assert.Contains("SCUM folder", vm.KeyStatus);
    }

    [AvaloniaFact]
    public async Task Testing_a_working_key_says_so()
    {
        var store = new InMemorySettingsStore();
        store.Save(new AppSettings(@"C:\w", @"D:\Games\SCUM", AesKey: "K"));
        var vm = new SettingsViewModel(store, new FakeScumSources(),
            new FakeContent(SCMW.Core.Content.ContentSourceState.Ready));

        await vm.TestKeyCommand.ExecuteAsync(null);

        Assert.Contains("works", vm.KeyStatus);
        Assert.True(vm.KeyTestGood);        // shown green
        Assert.False(vm.KeyTestBad);
    }

    [AvaloniaFact]
    public async Task Testing_a_bad_key_reports_it_did_not_work()
    {
        var store = new InMemorySettingsStore();
        store.Save(new AppSettings(@"C:\w", @"D:\Games\SCUM", AesKey: "K"));
        var vm = new SettingsViewModel(store, new FakeScumSources(),
            new FakeContent(SCMW.Core.Content.ContentSourceState.Error));

        await vm.TestKeyCommand.ExecuteAsync(null);

        Assert.Contains("didn't work", vm.KeyStatus);
        Assert.True(vm.KeyTestBad);         // shown red
        Assert.False(vm.KeyTestGood);
    }

    [AvaloniaFact]
    public async Task Editing_the_key_clears_an_old_test_result()
    {
        var store = new InMemorySettingsStore();
        store.Save(new AppSettings(@"C:\w", @"D:\Games\SCUM", AesKey: "K"));
        var vm = new SettingsViewModel(store, new FakeScumSources(),
            new FakeContent(SCMW.Core.Content.ContentSourceState.Ready));

        await vm.TestKeyCommand.ExecuteAsync(null);
        Assert.True(vm.KeyTestGood);

        vm.UnlockKey = "a different key";

        Assert.Empty(vm.KeyStatus);         // the old "works" does not linger
        Assert.False(vm.KeyTestGood);
    }

    [AvaloniaFact]
    public void Detect_again_reports_honestly_when_nothing_is_found()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());

        vm.DetectGameCommand.Execute(null);

        Assert.False(vm.GameFound);
        Assert.Equal("SCUM not found", vm.GameStatus);
        Assert.Contains("Browse", vm.GameHint);          // the engine's hint is now shown
    }

    [AvaloniaFact]
    public void Detection_changes_raise_an_event_for_the_header()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());
        var fired = 0;
        vm.GameChanged += () => fired++;

        vm.GamePath = @"D:\Games\SCUM";

        Assert.True(fired > 0);
    }

    [AvaloniaFact]
    public void The_storage_figure_measures_the_saved_game_data()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = SCMW.Core.Workspace.WorkspaceService.Ensure(dir);
            File.WriteAllBytes(Path.Combine(workspace.Cache, "assets.db"), new byte[2048]);

            var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
                workspace: workspace);

            Assert.Contains("KB", vm.StorageUsed);   // 2 KB, not "Not tracked yet"
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void Finishing_a_re_read_re_measures_the_saved_size()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = SCMW.Core.Workspace.WorkspaceService.Ensure(dir);
            var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
                workspace: workspace);
            Assert.Equal("Empty", vm.StorageUsed);            // nothing saved yet

            // The re-read writes the fresh item list...
            File.WriteAllBytes(Path.Combine(workspace.Cache, "assets.db"), new byte[4096]);

            // ...then the app reports it has finished.
            vm.RefreshFinished("Done — SCMW — Lone Wolf re-read your items from SCUM.", good: true);

            Assert.Contains("KB", vm.StorageUsed);            // measured now, not stuck on "Empty"
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void A_successful_re_read_shows_a_green_done_line()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());

        vm.RefreshFinished("Done — SCMW — Lone Wolf re-read your items from SCUM.", good: true);

        Assert.True(vm.CacheNoteGood);
        Assert.Contains("Done", vm.CacheNote);
    }

    [AvaloniaFact]
    public async Task Rebuilding_clears_the_saved_item_list_and_pictures()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = SCMW.Core.Workspace.WorkspaceService.Ensure(dir);
            File.WriteAllBytes(Path.Combine(workspace.Cache, "assets.db"), new byte[1024]);
            var icons = Path.Combine(workspace.Cache, "icons");
            Directory.CreateDirectory(icons);
            File.WriteAllBytes(Path.Combine(icons, "a.png"), new byte[10]);

            var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
                workspace: workspace);

            await vm.ClearCacheCommand.ExecuteAsync(null);

            Assert.False(File.Exists(Path.Combine(workspace.Cache, "assets.db")));
            Assert.False(Directory.Exists(icons));
            Assert.Equal("Empty", vm.StorageUsed);
            Assert.Contains("cleared", vm.CacheNote);

            // It says it is re-reading, and reports back when the app has finished.
            Assert.True(vm.IsRefreshing);
            Assert.Contains("Re-reading", vm.CacheNote);

            vm.RefreshFinished("Done — SCMW — Lone Wolf re-read your items from SCUM.");

            Assert.False(vm.IsRefreshing);
            Assert.Contains("Done", vm.CacheNote);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void A_build_id_change_is_written()
    {
        var store = new InMemorySettingsStore();
        var vm = new SettingsViewModel(store, new FakeScumSources());

        vm.BuildId = "513710-build";

        Assert.Equal("513710-build", store.Load().BuildId);
    }

    [AvaloniaFact]
    public void Detect_again_does_not_contradict_a_verified_folder()
    {
        var exe = @"D:\Games\SCUM/SCUM/Binaries/Win64/SCUM.exe";
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources((exe, true)));
        vm.GamePath = @"D:\Games\SCUM";
        Assert.True(vm.GameFound);

        // Steam can't see it (no library), but the folder IS a verified install.
        vm.DetectGameCommand.Execute(null);

        Assert.True(vm.GameFound);
        Assert.Equal("SCUM detected", vm.GameStatus);
    }

    [AvaloniaFact]
    public void An_in_memory_store_says_the_settings_are_not_being_saved()
    {
        var temporary = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());
        Assert.Contains("can't be saved", temporary.StorageNote);

        var dir = Path.Combine(Path.GetTempPath(), "scmw-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var real = new SettingsViewModel(new JsonSettingsStore(dir), new FakeScumSources());
            Assert.Empty(real.StorageNote);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Opening_the_workspace_folder_hands_the_path_to_the_file_browser()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-open-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = SCMW.Core.Workspace.WorkspaceService.Ensure(dir);
            var opener = new FakeFolderOpener();
            var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
                workspace: workspace, folderOpener: opener);

            vm.OpenWorkspaceCommand.Execute(null);

            Assert.Equal(workspace.Root, opener.Opened);
            Assert.Empty(vm.WorkspaceNote);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void Opening_the_game_folder_hands_the_scum_path_to_the_file_browser()
    {
        var opener = new FakeFolderOpener();
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            folderOpener: opener);
        vm.GamePath = @"D:\Games\SCUM";

        vm.OpenGameFolderCommand.Execute(null);

        Assert.Equal(@"D:\Games\SCUM", opener.Opened);
        Assert.Empty(vm.GameFolderNote);
    }

    [AvaloniaFact]
    public void Opening_a_folder_that_is_not_set_says_so_plainly()
    {
        var opener = new FakeFolderOpener { CanOpen = false };
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            folderOpener: opener);

        vm.OpenGameFolderCommand.Execute(null);
        Assert.Contains("Set your SCUM folder", vm.GameFolderNote);

        vm.OpenWorkspaceCommand.Execute(null);       // no workspace and no path set
        Assert.Contains("no folder", vm.WorkspaceNote);
    }

    [AvaloniaFact]
    public void The_mods_path_follows_the_game_folder()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources());
        Assert.Empty(vm.ModsPath);                   // nothing set yet

        vm.GamePath = @"C:\Games\SCUM";

        Assert.EndsWith(Path.Combine("SCUM", "Content", "Paks", "~mods"), vm.ModsPath);
    }

    [AvaloniaFact]
    public void Opening_the_mods_folder_hands_the_path_to_the_file_browser()
    {
        var opener = new FakeFolderOpener();
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            folderOpener: opener);
        vm.GamePath = @"C:\Games\SCUM";

        vm.OpenModsFolderCommand.Execute(null);

        Assert.EndsWith(Path.Combine("SCUM", "Content", "Paks", "~mods"), opener.Opened);
        Assert.Empty(vm.ModsNote);
    }

    [AvaloniaFact]
    public void Opening_the_mods_folder_with_no_game_says_so_plainly()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            folderOpener: new FakeFolderOpener());

        vm.OpenModsFolderCommand.Execute(null);

        Assert.Contains("Set your SCUM folder", vm.ModsNote);
    }

    [AvaloniaFact]
    public void The_log_folder_is_shown_and_can_be_opened()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = SCMW.Core.Workspace.WorkspaceService.Ensure(dir);
            var opener = new FakeFolderOpener();
            var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
                workspace: workspace, folderOpener: opener);

            Assert.Equal(workspace.Logs, vm.LogPath);

            vm.OpenLogsFolderCommand.Execute(null);

            Assert.Equal(workspace.Logs, opener.Opened);
            Assert.Empty(vm.LogNote);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void Opening_the_log_folder_without_a_workspace_says_so_plainly()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            folderOpener: new FakeFolderOpener());

        Assert.Empty(vm.LogPath);

        vm.OpenLogsFolderCommand.Execute(null);

        Assert.Contains("no log folder", vm.LogNote);
    }

    [AvaloniaFact]
    public void The_licence_files_folder_can_be_opened()
    {
        var opener = new FakeFolderOpener();
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            folderOpener: opener);

        vm.OpenLicenceFilesCommand.Execute(null);

        Assert.Equal(AppContext.BaseDirectory, opener.Opened);   // beside the app
        Assert.Empty(vm.LicenceNote);
    }

    [AvaloniaFact]
    public void The_settings_folder_can_be_opened()
    {
        var opener = new FakeFolderOpener();
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            folderOpener: opener);

        vm.OpenSettingsFolderCommand.Execute(null);

        Assert.Equal(AppPaths.Default.SettingsFolder, opener.Opened);
        Assert.Empty(vm.SettingsFolderNote);
    }

    /// <summary>Records what was asked to open; never launches Explorer.</summary>
    private sealed class FakeFolderOpener : IFolderOpener
    {
        public string? Opened;
        public bool CanOpen = true;

        public bool Open(string? path)
        {
            Opened = path;
            return CanOpen && !string.IsNullOrWhiteSpace(path);
        }
    }

    [AvaloniaFact]
    public void The_key_help_answers_and_the_About_Help_button_opens_the_Help_screen()
    {
        var nav = new NavigationHost();
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(), nav: nav);

        vm.WhereKeyCommand.Execute(null);
        vm.OpenHelpCommand.Execute(null);

        Assert.Contains("key list", vm.KeyHelpNote);     // real guidance, not a placeholder
        Assert.Equal(ScreenKey.Help, nav.Current);      // the Help button opens the Help screen
    }

    [AvaloniaFact]
    public void The_key_help_opens_a_public_list_and_the_discord()
    {
        var links = new FakeLinkOpener();
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            linkOpener: links);

        vm.WhereKeyCommand.Execute(null);
        Assert.Contains("Ctrl+F", vm.KeyHelpNote);       // the steps are there

        vm.OpenKeyListCommand.Execute(null);
        Assert.Equal(KeySources.PublicList, links.Opened);

        vm.OpenKeyFallbackCommand.Execute(null);
        Assert.Equal(KeySources.Fallback, links.Opened);

        Assert.Empty(vm.KeyLinkNote);
    }

    [AvaloniaFact]
    public void A_key_link_that_cannot_open_says_so_plainly()
    {
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            linkOpener: new FakeLinkOpener { CanOpen = false });

        vm.OpenKeyListCommand.Execute(null);

        Assert.Contains("couldn't be opened", vm.KeyLinkNote);
    }

    [AvaloniaFact]
    public void The_support_button_opens_the_ko_fi_page()
    {
        var links = new FakeLinkOpener();
        var vm = new SettingsViewModel(new InMemorySettingsStore(), new FakeScumSources(),
            linkOpener: links);

        Assert.True(vm.ShowSupport);
        vm.OpenSupportCommand.Execute(null);

        Assert.Equal(AppInfo.SupportUrl, links.Opened);
        Assert.Empty(vm.SupportNote);
    }

    /// <summary>Records what was asked to open; never launches a browser.</summary>
    private sealed class FakeLinkOpener : ILinkOpener
    {
        public string? Opened;
        public bool CanOpen = true;

        public bool Open(string? url)
        {
            Opened = url;
            return CanOpen && !string.IsNullOrWhiteSpace(url);
        }
    }

    [AvaloniaFact]
    public void Settings_survive_a_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scmw-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new SettingsViewModel(new JsonSettingsStore(dir), new FakeScumSources());
            first.WorkspacePath = @"C:\MyWork";
            first.UnlockKey = "KEY-9";

            // A brand-new store and screen, as if the app had been closed and opened again.
            var second = new SettingsViewModel(new JsonSettingsStore(dir), new FakeScumSources());

            Assert.Equal(@"C:\MyWork", second.WorkspacePath);
            Assert.Equal("KEY-9", second.UnlockKey);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
