using System;

namespace SCMW.App;

/// <summary>
/// Holds the current screen and raises <see cref="Changed"/> when it moves.
/// GoTo on the already-active key is a no-op. Use <see cref="Shared"/> in the app;
/// tests make their own host for isolation.
/// </summary>
public sealed class NavigationHost : INavigation
{
    /// <summary>App-wide instance (one per running app).</summary>
    public static NavigationHost Shared { get; } = new();

    private ScreenKey _current = ScreenKey.Home;

    public ScreenKey Current => _current;

    public event Action? Changed;

    public void GoTo(ScreenKey key)
    {
        if (key == _current)
        {
            return;
        }

        _current = key;
        Changed?.Invoke();
    }
}
