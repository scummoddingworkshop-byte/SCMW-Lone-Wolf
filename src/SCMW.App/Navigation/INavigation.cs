using System;

namespace SCMW.App;

/// <summary>Knows which area is currently shown and announces when it changes.</summary>
public interface INavigation
{
    ScreenKey Current { get; }

    event Action? Changed;

    void GoTo(ScreenKey key);
}
