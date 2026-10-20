using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LineMonitor.Core.Models;

namespace LineMonitor.App.Converters;

/// <summary>표시용 데이터만 반전하고 DB의 레인 번호와 좌우 값은 변경하지 않는다.</summary>
public sealed class MirrorMeasurementConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not true) return values.FirstOrDefault();
        if (values[0] is SideMeasurement measurement)
        {
            var mirrored = measurement with
            {
                Edges = measurement.Edges.Reverse().ToList(),
                Lanes = measurement.Lanes.Reverse().Select(lane => lane with
                {
                    InsulWetLeft = lane.InsulWetRight,
                    InsulWetRight = lane.InsulWetLeft,
                    InsulDryLeft = lane.InsulDryRight,
                    InsulDryRight = lane.InsulDryLeft
                }).ToList()
            };
            Debug.Assert(mirrored.Lanes.Select(lane => lane.Lane)
                .SequenceEqual(measurement.Lanes.Select(lane => lane.Lane).Reverse()));
            return mirrored;
        }
        if (values[0] is IEnumerable<LaneMismatch> lanes)
        {
            return lanes.Reverse().Select(lane => lane with { Left = lane.Right, Right = lane.Left }).ToList();
        }
        return values[0];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound value is non-null (a string is also "null" when empty). Pass ConverterParameter="Invert" to flip.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null && value is not "";
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows the sign of each edge mismatch with a small inset, not a physical mm scale.</summary>
public sealed class MismatchBarMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LaneMismatch lane) return new Thickness(4, 0, 4, 0);

        var isBack = string.Equals(parameter as string, "Back", StringComparison.Ordinal);
        var left = lane.Left.GetValueOrDefault();
        var right = lane.Right.GetValueOrDefault();
        return new Thickness(
            (isBack ? left < 0 : left > 0) ? 11 : 4, 0,
            (isBack ? right < 0 : right > 0) ? 11 : 4, 0);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToLiveBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x3A, 0xDB, 0x6A))
            : new SolidColorBrush(Color.FromRgb(0x8B, 0x96, 0xAB));

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound collection has at least one item.</summary>
public sealed class CollectionHasItemsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is System.Collections.ICollection { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Scales a coat/foil length (mm-ish) into a pixel width for the pattern strip diagram. ConverterParameter is the px-per-unit scale (default 2.5).</summary>
public sealed class LengthToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var length = value switch
        {
            decimal d => (double)d,
            double dd => dd,
            _ => 0d
        };

        var scale = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 2.5;
        return Math.Clamp(length * scale, 14, 260);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
