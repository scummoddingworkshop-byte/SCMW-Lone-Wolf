using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace SCMW.App.Tests;

public class ThemeTests
{
    [AvaloniaFact]
    public void Theme_exposes_the_expected_brushes()
    {
        var keys = new[]
        {
            "BgBaseBrush", "BgPanelBrush", "BgCardBrush", "BorderBrush", "TextPrimaryBrush",
            "TextDimBrush", "AccentBrush", "AccentHoverBrush", "OnAccentBrush", "SuccessBrush",
            "WarningBrush", "AccentSoftBrush",
        };

        foreach (var key in keys)
        {
            Assert.True(Application.Current!.TryFindResource(key, out _), $"missing {key}");
        }
    }

    /// <summary>
    /// Guards the design system itself: a control carrying a theme class must actually pick
    /// up the styled value, so deleting a style cannot pass unnoticed.
    /// </summary>
    [AvaloniaFact]
    public void Theme_exposes_component_styles()
    {
        var card = new Border { Classes = { "card" } };
        var primary = new Button { Classes = { "primary" } };

        var window = new Window { Content = new StackPanel { Children = { card, primary } } };
        window.Show();

        Assert.Equal(new CornerRadius(10), card.CornerRadius);

        var background = Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background);
        Assert.Equal(Color.Parse("#E07B39"), background.Color);
    }

    /// <summary>Spec §5: the quick-action card and the list row light up on hover.</summary>
    [AvaloniaFact]
    public void Quick_action_and_list_row_light_up_on_hover()
    {
        var quick = new Border { Classes = { "quickaction" } };
        var row = new Border { Classes = { "listrow" } };

        var window = new Window { Content = new StackPanel { Children = { quick, row } } };
        window.Show();

        var quickRest = quick.BorderBrush;
        var rowRest = row.Background;

        ((IPseudoClasses)quick.Classes).Set(":pointerover", true);
        ((IPseudoClasses)row.Classes).Set(":pointerover", true);

        Assert.NotEqual(quickRest, quick.BorderBrush);
        Assert.NotEqual(rowRest, row.Background);
    }
}
