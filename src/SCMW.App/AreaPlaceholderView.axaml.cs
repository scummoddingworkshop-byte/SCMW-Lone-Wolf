using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App;

/// <summary>
/// Simple "coming later" screen used for every area until Plans 2–4 replace them.
/// The title is pushed straight into the label (no DataContext binding), so the view
/// works the same whether or not a DataContext is set.
/// </summary>
public partial class AreaPlaceholderView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<AreaPlaceholderView, string>(nameof(Title), "");

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public AreaPlaceholderView()
    {
        AvaloniaXamlLoader.Load(this);
        UpdateTitle();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle() =>
        this.FindControl<TextBlock>("TitleText")!.Text = Title;
}
