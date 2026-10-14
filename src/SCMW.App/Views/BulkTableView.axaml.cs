using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The bulk table screen (Task 5.1): tick many files — or all the ones a category shows — and set
/// one value once. Reads everything from its view model.
/// </summary>
public partial class BulkTableView : UserControl
{
    public BulkTableView() => AvaloniaXamlLoader.Load(this);
}
