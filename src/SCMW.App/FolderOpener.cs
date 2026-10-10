using System.Diagnostics;
using System.IO;

namespace SCMW.App;

/// <summary>Opens a folder in the desktop's file browser. An interface so tests never launch Explorer.</summary>
public interface IFolderOpener
{
    /// <summary>Opens the folder; returns false when the path is empty or is not a real folder.</summary>
    bool Open(string? path);
}

/// <summary>The real opener: hands the folder to Windows (the same shell call the launcher uses).</summary>
public sealed class SystemFolderOpener : IFolderOpener
{
    public bool Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
