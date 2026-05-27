using Avalonia.Controls;
using Avalonia.Media;

namespace Genie.App.Controls;

/// <summary>
/// Shared helpers for binding Avalonia <c>ColorPicker</c> + a "Default/None"
/// <c>CheckBox</c> to the string-based color values used by the preset /
/// highlight / layout configuration (e.g. <c>"Default"</c>, <c>""</c>,
/// <c>"IndianRed"</c>, <c>"#FF8800"</c>).
///
/// Ported directly from dylb0t/Genie5's <c>Genie5.Ui.ColorPickerHelpers</c>
/// because it solves our exact ColorPicker-binding pain (storing a sentinel
/// keyword as a separate UI checkbox instead of overloading the hex string).
/// </summary>
internal static class ColorPickerHelpers
{
    /// <summary>Seeds the picker and checkbox from a stored color string.</summary>
    public static void LoadColor(ColorPicker picker, CheckBox sentinel,
                                 string value, string sentinelKeyword)
    {
        if (IsSentinel(value, sentinelKeyword))
        {
            sentinel.IsChecked = true;
            return;
        }

        sentinel.IsChecked = false;
        if (Color.TryParse(value, out var c))
            picker.Color = c;
    }

    /// <summary>
    /// Returns the string value to persist back to storage. When the sentinel
    /// checkbox is set, returns the sentinel keyword (e.g. <c>"Default"</c> for
    /// foregrounds, <c>""</c> for backgrounds). Otherwise <c>"#RRGGBB"</c>.
    /// </summary>
    public static string ReadColor(ColorPicker picker, CheckBox sentinel, string sentinelKeyword)
    {
        if (sentinel.IsChecked == true) return sentinelKeyword;
        var c = picker.Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static bool IsSentinel(string value, string sentinelKeyword)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (!string.IsNullOrEmpty(sentinelKeyword)
            && value.Equals(sentinelKeyword, StringComparison.OrdinalIgnoreCase))
            return true;
        return value.Equals("(none)", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve a stored colour string ("Default", "", "#RRGGBB", "Red",
    /// "IndianRed", "(none)" …) to an <see cref="IBrush"/>.  Returns null when
    /// the value is a sentinel meaning "use the system default" so callers can
    /// decide on their own fallback (e.g. <c>?? Brushes.LightGray</c>).
    /// </summary>
    public static IBrush? ParseBrush(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Equals("Default", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Equals("(none)",  StringComparison.OrdinalIgnoreCase)) return null;

        if (Color.TryParse(value, out var c))
            return new SolidColorBrush(c);

        try { return Brush.Parse(value); }
        catch { return null; }
    }
}
