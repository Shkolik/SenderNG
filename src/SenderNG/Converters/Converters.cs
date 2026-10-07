
/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Collections;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SenderNG.Converters;

public static class Converters
{
    public static HasElementsConverter HasElements => HasElementsConverter.Instance;
    public static LeftPluginsVisibilityConverter LeftPluginsVisible => LeftPluginsVisibilityConverter.Instance;
    public static BoolToSignalBrushConverter SignalBrush => BoolToSignalBrushConverter.Instance;
    public static IsFlyoutModuleConverter IsFlyout => IsFlyoutModuleConverter.Instance;
    public static NumericTextConverter NumericText => NumericTextConverter.Instance;
    public static LocaleDisplayNameConverter LocaleDisplayName => LocaleDisplayNameConverter.Instance;
}

/// <summary>
/// Two-way string&lt;-&gt;number converter for numeric text fields. Formats
/// doubles with "0.###" and silently ignores text that cannot be parsed
/// (returns <see cref="BindingOperations.DoNothing"/>), so partial or invalid
/// input never raises a binding validation error (red border) or an exception.
/// </summary>
public sealed class NumericTextConverter : IValueConverter
{
    public static readonly NumericTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double d => d.ToString("0.###", culture),
            int i => i.ToString(culture),
            _ => string.Empty,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s)
            return BindingOperations.DoNothing;
        if (targetType == typeof(int))
            return int.TryParse(s, NumberStyles.Integer, culture, out var i) ? i : BindingOperations.DoNothing;
        if (double.TryParse(s, NumberStyles.Float, culture, out var d))
            return d;
        return BindingOperations.DoNothing;
    }
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

public sealed class LeftPluginsVisibilityConverter : IValueConverter
{
    public static readonly LeftPluginsVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i) return i > 2;
        if (value is ICollection c) return c.Count > 2;
        if (value is IEnumerable e) return Count(e) > 2;
        return false;
    }

    private static int Count(IEnumerable e)
    {
        var count = 0;
        var en = e.GetEnumerator();
        try { while (en.MoveNext()) { count++; if (count > 2) break; } }
        finally { (en as IDisposable)?.Dispose(); }
        return count;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToSignalBrushConverter : IValueConverter
{
    public static readonly BoolToSignalBrushConverter Instance = new();
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#e04545"));
    private static readonly IBrush InactiveBrush = Brushes.Transparent;
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ActiveBrush : InactiveBrush;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IsFlyoutModuleConverter : IValueConverter
{
    public static readonly IsFlyoutModuleConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SenderNG.Core.Modules.IModule m)
            return m.Panel == SenderNG.Core.Modules.PanelHint.Flyout;
        return false;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Locale code → native language name for the language picker's ItemTemplate.
/// Non-string values pass through unchanged.
/// </summary>
public sealed class LocaleDisplayNameConverter : IValueConverter
{
    public static readonly LocaleDisplayNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string code ? SenderNG.Localization.LocaleDisplayNames.For(code) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
