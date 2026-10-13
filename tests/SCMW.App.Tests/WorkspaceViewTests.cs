using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SCMW.App.Controls;
using SCMW.App.Views;
using SCMW.Core;
using SCMW.Core.Content;
using SCMW.Core.Knowledge;

namespace SCMW.App.Tests;

/// <summary>
/// The adapting Workspace: one screen that shows a file's real fields, each explained
/// (label, unit, and a risk dot) from the field library.
/// </summary>
public class WorkspaceViewTests
{
    private static AssetField Field(string technical, string value) =>
        new(technical, technical, StatusKind.Editable, value);

    private static WorkspaceView Shown(out WorkspaceViewModel vm)
    {
        vm = new WorkspaceViewModel();
        vm.Show("Items", "Tomato", new[] { Field("Weight", "20000"), Field("MaxQuantity", "10") });
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 500 };
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void Renders_one_row_per_real_field_of_the_file()
    {
        var view = Shown(out _);

        var rows = view.GetVisualDescendants().OfType<FieldRow>().ToList();

        Assert.Equal(2, rows.Count);
    }

    [AvaloniaFact]
    public void A_row_shows_the_library_label_value_and_unit()
    {
        var view = Shown(out _);

        var weight = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => r.FindControl<TextBlock>("LabelText")!.Text == "Weight");

        Assert.Equal("20000", weight.FindControl<TextBlock>("ValueText")!.Text);
        Assert.Equal("grams", weight.FindControl<TextBlock>("UnitText")!.Text);
    }

    [AvaloniaFact]
    public void A_row_carries_the_fields_risk_for_its_dot()
    {
        var view = Shown(out _);

        var weight = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => r.FindControl<TextBlock>("LabelText")!.Text == "Weight");

        Assert.Equal(Risk.Low, ((WorkspaceField)weight.DataContext!).Risk);
        Assert.Contains("low", weight.FindControl<Border>("RiskDot")!.Classes);
    }

    [AvaloniaFact]
    public void The_library_label_wins_over_the_raw_technical_name()
    {
        var view = Shown(out _);

        var maxStack = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => ((WorkspaceField)r.DataContext!).TechnicalName == "MaxQuantity");

        Assert.Equal("Stack size", maxStack.FindControl<TextBlock>("LabelText")!.Text);
    }

    [AvaloniaFact]
    public void The_title_names_the_opened_file()
    {
        var view = Shown(out _);

        var titles = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("Tomato", titles);
    }

    [AvaloniaFact]
    public void A_field_the_library_does_not_describe_is_shown_honestly()
    {
        var vm = new WorkspaceViewModel();
        vm.Show("Items", "Odd thing", new[] { Field("SomeMysteryNumber", "42") });
        var view = new WorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 300 };
        window.Show();

        var row = view.GetVisualDescendants().OfType<FieldRow>().Single();
        var field = (WorkspaceField)row.DataContext!;

        Assert.False(field.Described);
        Assert.Equal(Risk.Unknown, field.Risk);
        Assert.True(row.IsVisible);                                  // never hidden
        Assert.Contains("unknown", row.FindControl<Border>("RiskDot")!.Classes);
        Assert.Equal(WorkspaceField.NotDescribed,
            row.FindControl<TextBlock>("MeaningText")!.Text);
        Assert.True(row.FindControl<TextBlock>("MeaningText")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_described_field_shows_its_plain_meaning_not_the_note()
    {
        var view = Shown(out _);

        var weight = view.GetVisualDescendants().OfType<FieldRow>()
            .Single(r => ((WorkspaceField)r.DataContext!).TechnicalName == "Weight");
        var field = (WorkspaceField)weight.DataContext!;
        var meaning = weight.FindControl<TextBlock>("MeaningText")!;

        Assert.True(field.Described);
        Assert.Equal(field.Meaning, meaning.Text);
        Assert.NotEqual(WorkspaceField.NotDescribed, meaning.Text);
    }
}
