using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SenderNG.Avalonia.Converters;

public static class Converters
{
    public static HasElementsConverter HasElements => HasElementsConverter.Instance;
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
            IEnumerable e => HasAny(e),
            null => false,
            _ => false
        };
    }

    private static bool HasAny(IEnumerable e)
    {
        var en = e.GetEnumerator();
        try { return en.MoveNext(); }
        finally { (en as IDisposable)?.Dispose(); }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
