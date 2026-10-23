using SCMW.Core;

namespace SCMW.App;

/// <summary>
/// One choice in the "Place" dropdown: where on the shelf the recipe goes. <see cref="Number"/> is
/// the order number the build writes (<c>DisplayPriority</c>); <see cref="AfterPriority"/> is the
/// neighbour it was worked out from (null for "at the end"), kept for the plain words and tests.
/// </summary>
public sealed record RecipePlaceChoice(int Number, string Label, int? AfterPriority) : IPickerItem;
