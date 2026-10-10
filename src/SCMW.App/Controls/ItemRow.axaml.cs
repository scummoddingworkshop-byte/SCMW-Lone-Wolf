using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SCMW.App.Controls;

/// <summary>One row in the Items list: picture (or default), friendly name, category.</summary>
public partial class ItemRow : UserControl
{
    public static readonly StyledProperty<ItemSummary?> ItemProperty =
        AvaloniaProperty.Register<ItemRow, ItemSummary?>(nameof(Item));

    public ItemSummary? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public ItemRow()
    {
        AvaloniaXamlLoader.Load(this);
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var item = Item;

        this.FindControl<TextBlock>("NameText")!.Text = item?.Name ?? "";
        this.FindControl<TextBlock>("CategoryText")!.Text = item?.Category ?? "";

        var picture = this.FindControl<Image>("Picture")!;
        var mark = this.FindControl<TextBlock>("DefaultMark")!;

        // A real icon (a cached PNG) wins; otherwise the item's own picture; otherwise the tile.
        var source = LoadIcon(item?.IconPath) ?? item?.Picture;
        picture.Source = source;
        picture.IsVisible = source is not null;
        mark.IsVisible = source is null;
    }

    /// <summary>Loads a cached icon PNG, tolerating a missing or unreadable file (returns null).</summary>
    private static IImage? LoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}
