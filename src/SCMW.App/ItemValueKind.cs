namespace SCMW.App;

/// <summary>
/// What kind of value a property holds. Number is the only one built today; Boolean and
/// Text are reserved for later, so adding them is not a rewrite.
/// </summary>
public enum ItemValueKind
{
    Number,
    Boolean,
    Text,
}
