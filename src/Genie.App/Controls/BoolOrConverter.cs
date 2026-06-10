using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace Genie.App.Controls;

/// <summary>
/// Multi-value converter that returns <c>true</c> when ANY bound source is
/// <c>true</c>. Used by the Mapper's auto-hiding Details flyout to keep the
/// panel open while it is either hovered (<c>IsPointerOver</c>) OR pinned
/// (<c>DetailsPinned</c>). Reference it from XAML via
/// <c>{x:Static controls:BoolOrConverter.Instance}</c>.
/// </summary>
public sealed class BoolOrConverter : IMultiValueConverter
{
    public static readonly BoolOrConverter Instance = new();

    public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
        => values.Any(v => v is true);
}
