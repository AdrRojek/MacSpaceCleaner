using System.Globalization;
using Avalonia.Data.Converters;
using MacSpaceCleaner.Core;

namespace MacSpaceCleaner.Converters;

public sealed class BytesToStringConverter : IValueConverter
{
    public static readonly BytesToStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long l)
            return ByteFormatter.Format(l);
        if (value is int i)
            return ByteFormatter.Format(i);
        return "—";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PercentConverter : IValueConverter
{
    public static readonly PercentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
            return $"{d:0.#}%";
        return "—";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
