using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SCMW.Core;

namespace SCMW.App.Controls;

/// <summary>
/// The app's one dropdown. It shows the chosen item, opens a fixed-width list (so the width never
/// jumps while scrolling), and lets the owner type to narrow the list. Every picker in the app uses
/// this, so they all look and behave the same. Choices are any <see cref="IPickerItem"/>.
/// </summary>
public partial class SearchPicker : UserControl
{
    /// <summary>How long after the list closes a click on the box is ignored — the click that closed
    /// it (a click-away) must not immediately reopen it.</summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(200);

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchPicker, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SearchPicker, object?>(nameof(SelectedItem),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SearchPicker, IDataTemplate?>(nameof(ItemTemplate));

    /// <summary>The choices to show.</summary>
    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The chosen item (two-way, so the screen can read and set it).</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>How each choice is drawn (in the box and in the list).</summary>
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Raised when the owner picks a choice (never for a programmatic set).</summary>
    public event EventHandler? SelectionChanged;

    private readonly Button? _toggle;
    private readonly TextBlock? _valueText;
    private readonly Popup? _pop;
    private readonly TextBox? _search;
    private readonly ListBox? _options;
    private DateTime _closedAt = DateTime.MinValue;

    public SearchPicker()
    {
        AvaloniaXamlLoader.Load(this);

        _toggle = this.FindControl<Button>("Toggle");
        _valueText = this.FindControl<TextBlock>("ValueText");
        _pop = this.FindControl<Popup>("Pop");
        _search = this.FindControl<TextBox>("Search");
        _options = this.FindControl<ListBox>("Options");

        if (_pop is not null && _toggle is not null)
        {
            _pop.PlacementTarget = _toggle;
            _pop.PropertyChanged += OnPopupPropertyChanged;
        }

        if (_search is not null)
        {
            // React to the Text property directly (its TextChanged event is raised later, asynchronously).
            _search.PropertyChanged += OnSearchPropertyChanged;
            _search.KeyDown += OnSearchKeyDown;
        }

        if (_options is not null) _options.SelectionChanged += OnOptionPicked;
    }

    private void OnSearchPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty) Refilter();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty || change.Property == ItemTemplateProperty) Refilter();
        if (change.Property == SelectedItemProperty) UpdateValueText();
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_pop is null) return;

        // The click that closed the list (a click-away on the box) must not reopen it straight away.
        if (DateTime.UtcNow - _closedAt < ReopenGuard) return;

        _pop.IsOpen = !_pop.IsOpen;
    }

    private void OnPopupPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Popup.IsOpenProperty || _pop is null) return;

        _toggle?.Classes.Set("open", _pop.IsOpen);
        if (_pop.IsOpen) Open();
        else _closedAt = DateTime.UtcNow;
    }

    /// <summary>Open the list: show everything, match the box's width, and put the cursor in the search.</summary>
    private void Open()
    {
        if (_search is not null) _search.Text = string.Empty;
        Refilter();
        if (_pop is not null && _toggle is not null) _pop.Width = _toggle.Bounds.Width;
        _search?.Focus();
    }

    /// <summary>Rebuild the shown list from the choices and the typed text.</summary>
    private void Refilter()
    {
        if (_options is null) return;

        var all = ItemsSource is null
            ? new List<IPickerItem>()
            : ItemsSource.Cast<object?>().OfType<IPickerItem>().ToList();

        _options.ItemTemplate = ItemTemplate;
        _options.ItemsSource = PickerFilter.Apply(all, _search?.Text);
    }

    private void OnOptionPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_options?.SelectedItem is not { } picked) return;

        SetCurrentValue(SelectedItemProperty, picked);
        UpdateValueText();
        if (_pop is not null) _pop.IsOpen = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_pop is not null) _pop.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _options is { ItemCount: > 0 })
        {
            _options.SelectedIndex = 0;   // take the first match
            e.Handled = true;
        }
    }

    private void UpdateValueText()
    {
        if (_valueText is not null)
            _valueText.Text = SelectedItem is IPickerItem item ? item.Label : string.Empty;
    }
}
