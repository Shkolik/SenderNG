using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SenderNG.Avalonia.Converters;

public static class Converters
{
    public static readonly IValueConverter HasElements = new HasElementsConverter();
}

public sealed class HasElementsConverter : IValueConverter
{
    public static readonly HasElementsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int i => i > 0,
            ICollection c => c.Count > 0,
            IEnumerable e => e.GetEnumerator().MoveNext(),
            null => false,
            _ => true
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
