namespace SCMW.Core.Settings;

// Note: BuildId carries a default so the brief's verbatim test call shape
// `new AppSettings(null, null, AesKey: key)` (named arg after positionals) compiles (C# CS7036).
// All brief-documented call patterns (3-positional, named-optional) behave identically.
public sealed record AppSettings(string? WorkspacePath, string? GamePath, string? BuildId = null,
                                 string? AesKey = null,
                                 string? ActiveProjectId = null,
                                 int? WindowX = null, int? WindowY = null,
                                 int? WindowWidth = null, int? WindowHeight = null,
                                 bool WindowMaximized = false,
                                 bool SkipIntroVideos = false,
                                 string? LastScreen = null);

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings s);
}
