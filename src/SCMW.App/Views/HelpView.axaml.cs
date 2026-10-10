using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Views;

/// <summary>
/// The Help screen: plain-language answers, grouped into expandable topics. Mostly static text; the
/// "Getting your unlock key" topic has two buttons that open the key sources in the browser.
/// </summary>
public partial class HelpView : UserControl
{
    private static readonly ILinkOpener Links = new SystemLinkOpener();

    public HelpView() => AvaloniaXamlLoader.Load(this);

    private void OnOpenKeyList(object? sender, RoutedEventArgs e) => Links.Open(KeySources.PublicList);

    private void OnOpenKeyFallback(object? sender, RoutedEventArgs e) => Links.Open(KeySources.Fallback);
}
