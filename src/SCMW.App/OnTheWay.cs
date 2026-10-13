namespace SCMW.App;

/// <summary>
/// The one message for the ladder beyond the basics (design §§3, 12). Kept in a single place, so the
/// "on the way" strip and every locked button say exactly the same thing, and the levels always
/// carry their plain meaning (a number alone means nothing to a player).
/// </summary>
public static class OnTheWay
{
    public const string Message =
        "Things you can do in this version: Level 1 change item values · " +
        "Level 2 modify an existing recipe · Level 3 add a new field to an object.   " +
        "Coming up: create a new recipe (Level 3), new categories and objects (Levels 4–5).";
}
