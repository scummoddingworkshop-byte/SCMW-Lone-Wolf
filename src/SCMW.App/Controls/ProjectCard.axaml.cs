using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Controls;

/// <summary>
/// One project tile: picture (or a themed default), name, when, and a short
/// description. Clickable via <see cref="Command"/>. Its content is static, so it is
/// safe to place inside a button.
/// </summary>
public partial class ProjectCard : UserControl
{
    public static readonly StyledProperty<ProjectSummary?> ProjectProperty =
        AvaloniaProperty.Register<ProjectCard, ProjectSummary?>(nameof(Project));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ProjectCard, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<ProjectCard, object?>(nameof(CommandParameter));

    public ProjectSummary? Project
    {
        get => GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public ProjectCard()
    {
        AvaloniaXamlLoader.Load(this);
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProjectProperty || change.Property == CommandProperty
            || change.Property == CommandParameterProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var project = Project;

        this.FindControl<TextBlock>("NameText")!.Text = project?.Name ?? "";
        this.FindControl<TextBlock>("WhenText")!.Text = project?.When ?? "";
        this.FindControl<TextBlock>("DescriptionText")!.Text = project?.Description ?? "";

        var picture = this.FindControl<Image>("Picture")!;
        picture.Source = project?.Picture;
        picture.IsVisible = project?.Picture is not null;

        this.FindControl<TextBlock>("DefaultMark")!.IsVisible = project?.Picture is null;

        var inner = this.FindControl<Button>("Inner")!;
        inner.Command = Command;
        inner.CommandParameter = CommandParameter;
    }
}
