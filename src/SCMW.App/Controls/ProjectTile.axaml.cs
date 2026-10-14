using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCMW.App.Controls;

/// <summary>
/// One project in the Projects grid. A plain surface (not a button) holding its
/// details, a Current chip, an Open button and a ⋯ menu — so nothing interactive
/// is nested inside anything else.
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
        var menuButton = this.FindControl<Button>("MenuButton");

        if (nameText is null || picture is null || currentChip is null || menuButton is null)
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

        Wire("OpenButton", OpenCommand);
        Wire("ConfirmDeleteButton", ConfirmDeleteCommand);
        Wire("CancelDeleteButton", CancelDeleteCommand);

        if (menuButton.Flyout is MenuFlyout menu)
        {
            var items = menu.Items.Cast<object>().OfType<MenuItem>().ToList();
            if (items.Count == 3)
            {
                items[0].Command = RenameCommand;
                items[1].Command = DuplicateCommand;
                items[2].Command = RequestDeleteCommand;
                foreach (var item in items)
                {
                    item.CommandParameter = project;
                }
            }
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
