namespace SCMW.App;

/// <summary>
/// The one message for the ladder beyond the basics (design §§3, 12). Kept in a single place, so the
/// "on the way" strip and every locked button say exactly the same thing, and the levels always
/// carry their plain meaning (a number alone means nothing to a player).
/// </summary>
public static class OnTheWay
{
    public const string Message =
        "Things you can do in this version: Change item values (Mod level 1) · " +
        "Change an existing recipe (Mod level 2) · Add a new field to an object (Mod level 3) · " +
        "Create a new recipe (Mod level 3) · Create a new category (Mod level 4) · " +
        "Put a recipe on a new menu section of your own (Mod level 4) · " +
        "Create a new item from one you already have (Mod level 5).";
}
