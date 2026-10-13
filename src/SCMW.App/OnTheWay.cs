namespace SCMW.App;

/// <summary>
/// The one message for the ladder beyond the basics (design §§3, 12). Kept in a single place, so the
/// "on the way" strip and every locked button say exactly the same thing, and the levels always
/// carry their plain meaning (a number alone means nothing to a player).
/// </summary>
public static class OnTheWay
{
    public const string Message =
        "Here now — Level 1 change values · Level 2 change a recipe · Level 3 add a field.   " +
        "On the way — new recipes, new categories and new objects.";
}
