using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace Genie.App.Controls;

/// <summary>
/// Attached property that lets a binding push a list of <see cref="Inline"/>s
/// into a <see cref="TextBlock.Inlines"/> collection. Used to render highlighted
/// game text — each <see cref="Inline"/> represents a styled segment.
///
/// Usage (AXAML):
///   &lt;TextBlock controls:InlinesBehavior.Source="{Binding Inlines}"/&gt;
/// </summary>
public static class InlinesBehavior
{
    public static readonly AttachedProperty<IEnumerable<Inline>?> SourceProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IEnumerable<Inline>?>(
            "Source", typeof(InlinesBehavior));

    static InlinesBehavior()
    {
        SourceProperty.Changed.AddClassHandler<TextBlock>(OnSourceChanged);
    }

    public static IEnumerable<Inline>? GetSource(TextBlock tb)         => tb.GetValue(SourceProperty);
    public static void                 SetSource(TextBlock tb, IEnumerable<Inline>? v) => tb.SetValue(SourceProperty, v);

    private static void OnSourceChanged(TextBlock tb, AvaloniaPropertyChangedEventArgs e)
    {
        var inlines = tb.Inlines;
        if (inlines is null) return;

        inlines.Clear();
        if (e.NewValue is IEnumerable<Inline> source)
        {
            foreach (var inline in source)
                inlines.Add(inline);
        }
    }
}
