using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using SCMW.App.Controls;

namespace SCMW.App.Tests;

public class ModRowTests
{
    private static (ModRow row, InstalledMod mod) Shown(InstalledMod mod)
    {
        var row = new ModRow { DataContext = mod };
        var window = new Window { Content = row, Width = 600, Height = 120 };
        window.Show();
        return (row, mod);
    }

    [AvaloniaFact]
    public void Row_shows_the_name_the_badge_and_the_switch()
    {
        var (row, _) = Shown(new InstalledMod("My first mod", external: false));

        Assert.Equal("My first mod", row.FindControl<TextBlock>("NameText")!.Text);
        Assert.Equal("SCMW", row.FindControl<TextBlock>("BadgeText")!.Text);
        Assert.True(row.FindControl<ToggleSwitch>("Switch")!.IsChecked);
    }

    [AvaloniaFact]
    public void Row_shows_an_external_badge()
    {
        var (row, _) = Shown(new InstalledMod("Some.pak", external: true));

        Assert.Equal("External", row.FindControl<TextBlock>("BadgeText")!.Text);
    }

    [AvaloniaFact]
    public void The_switch_turns_the_mod_off()
    {
        var (row, mod) = Shown(new InstalledMod("Some.pak", external: true));

        row.FindControl<ToggleSwitch>("Switch")!.IsChecked = false;

        Assert.False(mod.IsOn);
    }

    [AvaloniaFact]
    public void A_switched_off_mod_is_dimmed()
    {
        var (row, mod) = Shown(new InstalledMod("Some.pak", external: true));
        var name = row.FindControl<TextBlock>("NameText")!;
        Assert.DoesNotContain("off", name.Classes);

        mod.IsOn = false;

        Assert.Contains("off", name.Classes);
    }

    [AvaloniaFact]
    public void Row_asks_before_removing()
    {
        var (row, mod) = Shown(new InstalledMod("Some.pak", external: true));

        Assert.False(row.FindControl<StackPanel>("ConfirmBar")!.IsVisible);

        mod.IsConfirmingRemove = true;

        Assert.True(row.FindControl<StackPanel>("ConfirmBar")!.IsVisible);
    }

    [AvaloniaFact]
    public void Row_contains_no_button_inside_a_button()
    {
        var (row, _) = Shown(new InstalledMod("Some.pak", external: true));

        foreach (var button in row.GetLogicalDescendants().OfType<Button>())
        {
            Assert.Empty(button.GetLogicalDescendants().OfType<Button>());
        }
    }
}
