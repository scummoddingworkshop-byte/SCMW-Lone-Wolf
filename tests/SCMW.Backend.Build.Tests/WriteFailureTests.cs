using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

public class WriteFailureTests
{
    [Fact]
    public void Windows_blocking_the_write_is_a_permission_problem()
        => Assert.Equal(WriteFailureKind.PermissionDenied,
            WriteFailure.Classify(new UnauthorizedAccessException("denied")));

    [Fact]
    public void A_locked_file_is_the_game_holding_it()
        => Assert.Equal(WriteFailureKind.GameRunning,
            WriteFailure.Classify(new IOException("the file is in use")));

    [Fact]
    public void An_access_denied_io_error_is_still_a_permission_problem()
    {
        // Windows sometimes reports ERROR_ACCESS_DENIED as an IOException rather than
        // UnauthorizedAccessException; it must not be mistaken for the game holding the file.
        var denied = new IOException("Access is denied.", unchecked((int)0x80070005));

        Assert.Equal(WriteFailureKind.PermissionDenied, WriteFailure.Classify(denied));
    }

    [Fact]
    public void The_permission_message_tells_the_player_to_restart_as_administrator()
    {
        var message = WriteFailure.Message(WriteFailureKind.PermissionDenied, "install the mod");

        Assert.Contains("administrator", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install the mod", message);
        Assert.DoesNotContain("close", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_busy_message_asks_the_player_to_close_the_game()
    {
        var message = WriteFailure.Message(WriteFailureKind.GameRunning, "install the mod");

        Assert.Contains("close", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install the mod", message);
    }
}
