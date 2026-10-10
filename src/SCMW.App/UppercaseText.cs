using Avalonia;
using Avalonia.Controls;

namespace SCMW.App;

/// <summary>
/// Shows a TextBlock's text in CAPITALS. Avalonia has no text-transform, so a heading opts in with
/// <c>UppercaseText.Enabled="True"</c> — the design system's <c>.section</c> style does this, so
/// every section heading is covered. Works for fixed text and bound text alike.
/// </summary>
public static class UppercaseText
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("Enabled", typeof(UppercaseText));

    public static void SetEnabled(TextBlock target, bool value) => target.SetValue(EnabledProperty, value);

    public static bool GetEnabled(TextBlock target) => target.GetValue(EnabledProperty);

    static UppercaseText()
    {
        EnabledProperty.Changed.AddClassHandler<TextBlock>((block, args) =>
        {
            block.PropertyChanged -= OnTextChanged;
            if (args.NewValue is true)
            {
                Apply(block);
                block.PropertyChanged += OnTextChanged;
            }
        });
    }

    private static void OnTextChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBlock.TextProperty && sender is TextBlock block) Apply(block);
    }

    /// <summary>Replaces the text with its capitals; setting it again settles (no loop).</summary>
    private static void Apply(TextBlock block)
    {
        if (block.Text is not { } text) return;

        var capitals = text.ToUpperInvariant();
        if (capitals != text) block.SetCurrentValue(TextBlock.TextProperty, capitals);
    }
}
