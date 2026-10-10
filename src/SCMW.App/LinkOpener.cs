using System;
using System.Diagnostics;

namespace SCMW.App;

/// <summary>Opens a web link in the default browser. An interface so tests never launch a browser.</summary>
public interface ILinkOpener
{
    /// <summary>Opens the link; returns false when it is empty, not a web link, or fails.</summary>
    bool Open(string? url);
}

/// <summary>The real opener: hands an http(s) link to Windows, the same shell call the launcher uses.</summary>
public sealed class SystemLinkOpener : ILinkOpener
{
    public bool Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;   // only ever open web links
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
