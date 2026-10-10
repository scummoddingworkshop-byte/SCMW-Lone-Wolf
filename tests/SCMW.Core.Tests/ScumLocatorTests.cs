using SCMW.Core.Setup;
using Xunit;

public class ScumLocatorTests
{
    private sealed class FakeSources : IScumSources
    {
        public List<string> Roots = new();
        public Dictionary<string, string> Files = new(); // virtual vfs
        public IReadOnlyList<string> SteamRoots() => Roots;
        // Brief's original join $"{dir}\\{file}" could never match this fake's own forward-slash
        // virtual keys ("d\file" can never equal "d/file"), so the join uses '/' like all keys here.
        public string? ReadAcf(string dir, string file) => Files.TryGetValue($"{dir}/{file}", out var v) ? v : null;
        public bool FileExists(string p) => Files.ContainsKey(p);
    }

    [Fact]
    public void Finds_via_appmanifest_when_present()
    {
        var s = new FakeSources { Roots = { "C:/Steam" } };
        s.Files[$"C:/Steam/steamapps/libraryfolders.vdf"] = "\"libraryfolders\"\n{ \"1\" { \"path\" \"C:/Games\" } }";
        s.Files[$"C:/Games/steamapps/appmanifest_513710.acf"] = "\"AppState\"\n{ \"name\" \"SCUM\"\n\"installdir\" \"SCUM\"\n\"buildid\" \"125621\" }";
        s.Files["C:/Games/steamapps/common/SCUM/SCUM/Binaries/Win64/SCUM.exe"] = "#";

        var r = ScumLocator.Locate(s);
        Assert.True(r.Found);
        Assert.Equal("C:/Games/steamapps/common/SCUM", r.Path);
        Assert.Equal("125621", r.BuildId);
    }

    [Fact] // real SCUM layout: <library>\common\SCUM\SCUM\Binaries\Win64\SCUM.exe (double SCUM)
    public void Marker_matches_real_scum_layout_double_scum()
    {
        var s = new FakeSources { Roots = { "C:/Steam" } };
        s.Files["C:/Steam/steamapps/libraryfolders.vdf"] = "\"libraryfolders\"\n{ \"1\" { \"path\" \"C:/Games\" } }";
        s.Files["C:/Games/steamapps/appmanifest_513710.acf"] = "\"AppState\"\n{ \"name\" \"SCUM\"\n\"installdir\" \"SCUM\"\n\"buildid\" \"125621\" }";
        s.Files["C:/Games/steamapps/common/SCUM/SCUM/Binaries/Win64/SCUM.exe"] = "#";

        var r = ScumLocator.Locate(s);
        Assert.True(r.Found);
        Assert.Equal("C:/Games/steamapps/common/SCUM", r.Path);
        Assert.Equal("125621", r.BuildId);
    }

    [Fact]
    public void No_steam_roots_means_not_found_with_hint()
    {
        var r = ScumLocator.Locate(new FakeSources());
        Assert.False(r.Found);
        Assert.Contains("browse", r.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // Review Focus #1 companion (service level): marker missing => not certain
    public void Manifest_without_marker_files_is_not_certain()
    {
        var s = new FakeSources { Roots = { "C:/Steam" } };
        s.Files["C:/Steam/steamapps/appmanifest_513710.acf"] = "\"AppState\"\n{ \"installdir\" \"SCUM\" }";
        var r = ScumLocator.Locate(s);
        Assert.False(r.Found); // unverified install -> user must browse
    }

    [Fact] // root itself is a library: appmanifest directly under <root>\steamapps is checked even without libraries.vdf
    public void Root_itself_is_a_library()
    {
        var s = new FakeSources { Roots = { "C:/Steam" } };
        s.Files["C:/Steam/steamapps/appmanifest_513710.acf"] = "\"AppState\"\n{ \"installdir\" \"SCUM\"\n\"buildid\" \"42\" }";
        s.Files["C:/Steam/steamapps/common/SCUM/SCUM/Binaries/Win64/SCUM.exe"] = "#";

        var r = ScumLocator.Locate(s);
        Assert.True(r.Found);
        Assert.Equal("C:/Steam/steamapps/common/SCUM", r.Path);
        Assert.Equal("42", r.BuildId);
    }

    [Fact] // manifest found but marker missing -> Found=false, hint indicates manual browse
    public void Manifest_without_marker_hints_browse()
    {
        var s = new FakeSources { Roots = { "C:/Steam" } };
        s.Files["C:/Steam/steamapps/appmanifest_513710.acf"] = "\"AppState\"\n{ \"installdir\" \"SCUM\"\n\"buildid\" \"99\" }";
        var r = ScumLocator.Locate(s);
        Assert.False(r.Found);
        Assert.Contains("browse", r.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // first verified candidate wins across multiple libraries
    public void First_verified_candidate_wins()
    {
        var s = new FakeSources { Roots = { "C:/Steam" } };
        s.Files["C:/Steam/steamapps/libraryfolders.vdf"] =
            "\"libraryfolders\"\n{ \"1\" { \"path\" \"C:/LibA\" } \"2\" { \"path\" \"C:/LibB\" } }";
        s.Files["C:/LibA/steamapps/appmanifest_513710.acf"] =
            "\"AppState\"\n{ \"name\" \"SCUM\"\n\"installdir\" \"SCUM\"\n\"buildid\" \"111\" }"; // stale install, no marker
        s.Files["C:/LibB/steamapps/appmanifest_513710.acf"] =
            "\"AppState\"\n{ \"name\" \"SCUM\"\n\"installdir\" \"SCUM\"\n\"buildid\" \"222\" }";
        s.Files["C:/LibB/steamapps/common/SCUM/SCUM/Binaries/Win64/SCUM.exe"] = "#";

        var r = ScumLocator.Locate(s);
        Assert.True(r.Found);
        Assert.Equal("C:/LibB/steamapps/common/SCUM", r.Path);
        Assert.Equal("222", r.BuildId);
    }

    [Fact] // manifest missing buildid is tolerated
    public void Manifest_without_buildid_still_locates()
    {
        var s = new FakeSources { Roots = { "C:/Steam" } };
        s.Files["C:/Steam/steamapps/appmanifest_513710.acf"] = "\"AppState\"\n{ \"installdir\" \"SCUM\" }";
        s.Files["C:/Steam/steamapps/common/SCUM/SCUM/Binaries/Win64/SCUM.exe"] = "#";

        var r = ScumLocator.Locate(s);
        Assert.True(r.Found);
        Assert.Equal("C:/Steam/steamapps/common/SCUM", r.Path);
        Assert.Equal(string.Empty, r.BuildId);
    }

    [Fact] // marker-only scan hook: VerifyInstalledDir checks the SCUM.exe file
    public void VerifyInstalledDir_checks_marker()
    {
        var s = new FakeSources();
        s.Files["D:/Manual/SCUM/SCUM/Binaries/Win64/SCUM.exe"] = "#";
        Assert.True(ScumLocator.VerifyInstalledDir("D:/Manual/SCUM", s));
        Assert.False(ScumLocator.VerifyInstalledDir("D:/Manual", s));
    }
}

public class AcfParserTests
{
    [Fact]
    public void Parses_libraries_vdf_paths()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        var paths = AcfParser.ReadValues(vdf, "path");
        Assert.Equal(2, paths.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", paths[0]);
        Assert.Equal(@"D:\SteamLibrary", paths[1]);
    }

    [Fact]
    public void Parses_appmanifest_installdir_and_buildid()
    {
        const string acf = "\"AppState\"\r\n{\r\n\t\"appid\"\t\t\"513710\"\r\n\t\"name\"\t\t\"SCUM\"\r\n\t\"buildid\"\t\t\"125621\"\r\n\t\"installdir\"\t\t\"SCUM\"\r\n\t\"LastPlayed\"\t\t\"0\"\r\n\t\"SizeOnDisk\"\t\t\"13371606066\"\r\n}";
        Assert.Equal("SCUM", AcfParser.ReadValue(acf, "installdir"));
        Assert.Equal("125621", AcfParser.ReadValue(acf, "buildid"));
    }

    [Fact]
    public void Tolerates_crlf_and_nested_braces()
    {
        const string acf = "\"AppState\"\r\n{\r\n\"UserConfig\"\r\n{\r\n\"language\" \"english\"\r\n}\r\n\"installdir\" \"SCUM\"\r\n}";
        Assert.Equal("SCUM", AcfParser.ReadValue(acf, "installdir"));
    }

    [Fact]
    public void Missing_key_returns_null()
    {
        Assert.Null(AcfParser.ReadValue("\"AppState\"\n{ \"installdir\" \"SCUM\" }", "buildid"));
        Assert.Null(AcfParser.ReadValue("garbage\"", "buildid"));
    }

    [Fact]
    public void ReadValues_is_empty_when_key_absent()
    {
        Assert.Empty(AcfParser.ReadValues("\"AppState\"\n{ \"installdir\" \"SCUM\" }", "path"));
    }
}
