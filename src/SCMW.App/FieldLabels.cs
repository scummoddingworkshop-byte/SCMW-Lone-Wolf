using SCMW.Core.Content;
using SCMW.Core.Knowledge;

namespace SCMW.App;

/// <summary>
/// The one place a changed value's name is decided for a screen: the field library's own label
/// ("Height in grid") when it describes the field, and the plainer words of <see cref="ChangeWords"/>
/// when it does not. Keeping it here means Build and Projects can never name the same value two
/// different ways.
/// </summary>
public static class FieldLabels
{
    private static readonly FieldLibrary Library = FieldLibraryLoader.LoadDefault();

    /// <summary>The words to show for a value's technical name.</summary>
    public static string Of(string? technicalName) =>
        Library.LabelFor(technicalName) ?? ChangeWords.For(technicalName);
}
