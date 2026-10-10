using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace SCMW.App.Controls;

/// <summary>
/// Button / rail-row with an icon slot on the left and a label on the right.
/// The slot keeps its size whether or not an icon is supplied, so adding icons later
/// (each item its own) never shifts the layout.
/// </summary>
public partial class IconButton : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<IconButton, string>(nameof(Text), "");

    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<IconButton, IImage?>(nameof(Icon));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<IconButton, ICommand?>(nameof(Command));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public IconButton()
    {
        AvaloniaXamlLoader.Load(this);
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty ||
            change.Property == IconProperty ||
            change.Property == CommandProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        this.FindControl<TextBlock>("LabelText")!.Text = Text;

        var image = this.FindControl<Image>("IconImage")!;
        image.Source = Icon;
        image.IsVisible = Icon is not null;

        this.FindControl<Button>("Inner")!.Command = Command;
    }
}
