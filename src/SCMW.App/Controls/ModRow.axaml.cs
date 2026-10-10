using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Controls;

/// <summary>
/// One installed mod: name, badge, on/off switch, and Remove with its inline confirmation.
/// Its DataContext is the InstalledMod. A plain surface, so nothing is nested.
/// </summary>
public partial class ModRow : UserControl
{
    public static readonly StyledProperty<ICommand?> RequestRemoveCommandProperty =
        AvaloniaProperty.Register<ModRow, ICommand?>(nameof(RequestRemoveCommand));

    public static readonly StyledProperty<ICommand?> ConfirmRemoveCommandProperty =
        AvaloniaProperty.Register<ModRow, ICommand?>(nameof(ConfirmRemoveCommand));

    public static readonly StyledProperty<ICommand?> CancelRemoveCommandProperty =
        AvaloniaProperty.Register<ModRow, ICommand?>(nameof(CancelRemoveCommand));

    public ICommand? RequestRemoveCommand
    {
        get => GetValue(RequestRemoveCommandProperty);
        set => SetValue(RequestRemoveCommandProperty, value);
    }

    public ICommand? ConfirmRemoveCommand
    {
        get => GetValue(ConfirmRemoveCommandProperty);
        set => SetValue(ConfirmRemoveCommandProperty, value);
    }

    public ICommand? CancelRemoveCommand
    {
        get => GetValue(CancelRemoveCommandProperty);
        set => SetValue(CancelRemoveCommandProperty, value);
    }

    public ModRow() => AvaloniaXamlLoader.Load(this);
}
