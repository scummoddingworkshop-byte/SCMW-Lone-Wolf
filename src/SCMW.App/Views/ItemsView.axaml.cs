using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Items screen: find on the left (search, category chips, only-changeable), work on
/// the right (the item's values, edited inline). Reads everything from its view model.
/// </summary>
public partial class ItemsView : UserControl
{
    public ItemsView() => AvaloniaXamlLoader.Load(this);
}
