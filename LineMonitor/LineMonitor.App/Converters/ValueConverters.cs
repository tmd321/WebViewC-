using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LineMonitor.Core.Models;

namespace LineMonitor.App.Converters;

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

public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => LoadImage(value as string);

    public static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or FormatException
            or InvalidOperationException or System.Security.SecurityException)
        {
            return null;
        }
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

/// <summary>Colors a signed numeric value: positive = accent (TOP이 큼), negative = warn (BACK이 큼).</summary>
public sealed class SignToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var d = value switch
        {
            decimal m => (double)m,
            double dd => dd,
            _ => 0d
        };

        if (d > 0) return new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0xC6));
        if (d < 0) return new SolidColorBrush(Color.FromRgb(0xE5, 0xA2, 0x3D));
        return new SolidColorBrush(Color.FromRgb(0x8B, 0x96, 0xAB));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
