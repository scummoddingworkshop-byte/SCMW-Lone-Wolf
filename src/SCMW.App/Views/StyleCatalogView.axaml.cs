using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// A hidden, review-only screen showing every design-system component side by side, so the
/// look can be judged at a glance. Not part of the rail; kept for future style work.
/// </summary>
public partial class StyleCatalogView : UserControl
{
    public StyleCatalogView() => AvaloniaXamlLoader.Load(this);
}
