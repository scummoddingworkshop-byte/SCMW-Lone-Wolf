namespace SCMW.Backend.Build;

/// <summary>
/// Why a write into SCUM's folder failed, in the two cases a player can act on.
/// </summary>
public enum WriteFailureKind
{
    /// <summary>It worked — not a failure.</summary>
    None,

    /// <summary>The file was locked, so the game is probably still running.</summary>
    GameRunning,

    /// <summary>Windows blocked the write — usually a protected folder like Program Files.</summary>
    PermissionDenied,
}

/// <summary>
/// Turns a low-level write error into a plain reason and message. One place, so Build, Mods and
/// Backups all tell the player the same honest story: a permission block is not "close the game".
/// </summary>
public static class WriteFailure
{
    /// <summary>Reads the real Windows error out of the exception.</summary>
    public static WriteFailureKind Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => WriteFailureKind.PermissionDenied,
        // Windows sometimes reports ERROR_ACCESS_DENIED (0x80070005) as a plain IOException.
        IOException io when io.HResult == unchecked((int)0x80070005) => WriteFailureKind.PermissionDenied,
        _ => WriteFailureKind.GameRunning,
    };

    /// <summary>A plain message naming what could not happen and what to do about it.</summary>
    public static string Message(WriteFailureKind kind, string action) => kind switch
    {
        WriteFailureKind.PermissionDenied =>
            $"Windows blocked SCMW from writing to your SCUM folder, so it couldn't {action}. " +
            "This usually means SCUM is installed in a protected place like Program Files. " +
            "Restart SCMW as administrator, then try again.",
        _ =>
            $"SCUM's files were busy, so SCMW couldn't {action}. " +
            "The game is probably still running — close it, then try again.",
    };
}
