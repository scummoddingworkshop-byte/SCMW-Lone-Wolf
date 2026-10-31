using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace SCMW.App.Controls;

/// <summary>
/// One project in the Projects grid. A plain surface (not a button) holding its details, a Current
/// chip, and its own Edit / Duplicate / Delete buttons. Clicking the card itself picks that project;
/// nothing interactive is nested inside anything else.
/// </summary>
public partial class ProjectTile : UserControl
{
    public static readonly StyledProperty<ProjectSummary?> ProjectProperty =
        AvaloniaProperty.Register<ProjectTile, ProjectSummary?>(nameof(Project));

    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<ProjectTile, ICommand?>(nameof(OpenCommand));

    public static readonly StyledProperty<ICommand?> RenameCommandProperty =
        AvaloniaProperty.Register<ProjectTile, ICommand?>(nameof(RenameCommand));

    public static readonly StyledProperty<ICommand?> DuplicateCommandProperty =
        AvaloniaProperty.Register<ProjectTile, ICommand?>(nameof(DuplicateCommand));

    public static readonly StyledProperty<ICommand?> RequestDeleteCommandProperty =
        AvaloniaProperty.Register<ProjectTile, ICommand?>(nameof(RequestDeleteCommand));

    public static readonly StyledProperty<ICommand?> ConfirmDeleteCommandProperty =
        AvaloniaProperty.Register<ProjectTile, ICommand?>(nameof(ConfirmDeleteCommand));

    public static readonly StyledProperty<ICommand?> CancelDeleteCommandProperty =
        AvaloniaProperty.Register<ProjectTile, ICommand?>(nameof(CancelDeleteCommand));

    public ProjectSummary? Project
    {
        get => GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    public ICommand? RenameCommand
    {
        get => GetValue(RenameCommandProperty);
        set => SetValue(RenameCommandProperty, value);
    }

    public ICommand? DuplicateCommand
    {
        get => GetValue(DuplicateCommandProperty);
        set => SetValue(DuplicateCommandProperty, value);
    }

    public ICommand? RequestDeleteCommand
    {
        get => GetValue(RequestDeleteCommandProperty);
        set => SetValue(RequestDeleteCommandProperty, value);
    }

    public ICommand? ConfirmDeleteCommand
    {
        get => GetValue(ConfirmDeleteCommandProperty);
        set => SetValue(ConfirmDeleteCommandProperty, value);
    }

    public ICommand? CancelDeleteCommand
    {
        get => GetValue(CancelDeleteCommandProperty);
        set => SetValue(CancelDeleteCommandProperty, value);
    }

    private bool _ready;

    public ProjectTile()
    {
        AvaloniaXamlLoader.Load(this);
        _ready = true;
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ProjectProperty)
        {
            if (change.OldValue is ProjectSummary old) old.PropertyChanged -= OnProjectChanged;
            if (change.NewValue is ProjectSummary fresh) fresh.PropertyChanged += OnProjectChanged;
        }

        if (_ready)
        {
            Update();
        }
    }

    private void OnProjectChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var project = Project;

        var nameText = this.FindControl<TextBlock>("NameText");
        var whenText = this.FindControl<TextBlock>("WhenText");
        var changesText = this.FindControl<TextBlock>("ChangesText");
        var descriptionText = this.FindControl<TextBlock>("DescriptionText");
        var picture = this.FindControl<Image>("Picture");
        var defaultMark = this.FindControl<TextBlock>("DefaultMark");
        var currentChip = this.FindControl<Border>("CurrentChip");
        var confirmBar = this.FindControl<StackPanel>("ConfirmBar");
        var actionsBar = this.FindControl<StackPanel>("ActionsBar");

        if (nameText is null || picture is null || currentChip is null || actionsBar is null)
        {
            return;   // not fully loaded yet
        }

        nameText.Text = project?.Name ?? "";
        whenText!.Text = project?.When ?? "";
        changesText!.Text = project?.ChangeCountText ?? "";
        descriptionText!.Text = project?.Description ?? "";

        picture.Source = project?.Picture;
        picture.IsVisible = project?.Picture is not null;
        defaultMark!.IsVisible = project?.Picture is null;

        currentChip.IsVisible = project?.IsCurrent == true;
        confirmBar!.IsVisible = project?.IsConfirmingDelete == true;

        // With no commands wired in, show no action buttons rather than dead ones.
        actionsBar.IsVisible = RenameCommand is not null || DuplicateCommand is not null
                               || RequestDeleteCommand is not null;

        Wire("EditButton", RenameCommand);
        Wire("DuplicateButton", DuplicateCommand);
        Wire("DeleteButton", RequestDeleteCommand);
        Wire("ConfirmDeleteButton", ConfirmDeleteCommand);
        Wire("CancelDeleteButton", CancelDeleteCommand);
    }

    /// <summary>Clicking the card itself picks that project (the screen stays where it is). A click
    /// on one of the card's own buttons is left to the button.</summary>
    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (OpenCommand?.CanExecute(Project) == true)
        {
            OpenCommand.Execute(Project);
            e.Handled = true;
        }
    }

    private void Wire(string name, ICommand? command)
    {
        var button = this.FindControl<Button>(name);
        if (button is null) return;
        button.Command = command;
        button.CommandParameter = Project;
    }
}
