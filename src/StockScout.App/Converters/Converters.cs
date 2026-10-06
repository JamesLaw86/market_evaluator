using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace StockScout.App.Converters;

/// <summary>Conviction 1-10 to a badge colour: amber (speculative), blue (moderate), green (strong).</summary>
public sealed class ConvictionToBrushConverter : IValueConverter
{
    private static readonly Brush Low = Frozen("#D97706");
    private static readonly Brush Mid = Frozen("#2563EB");
    private static readonly Brush High = Frozen("#059669");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int c ? c >= 8 ? High : c >= 6 ? Mid : Low : Mid;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>Positive numbers green, negative red.</summary>
public sealed class SignToBrushConverter : IValueConverter
{
    private static readonly Brush Up = ConvictionToBrushConverter.Frozen("#059669");
    private static readonly Brush Down = ConvictionToBrushConverter.Frozen("#DC2626");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && d < 0 ? Down : Up;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
