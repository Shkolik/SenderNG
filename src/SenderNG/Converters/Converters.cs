
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
using SenderNG.Core.Modules.Probing;

namespace SenderNG.Converters;

public static class Converters
{
    public static HasElementsConverter HasElements => HasElementsConverter.Instance;
    public static LeftPluginsVisibilityConverter LeftPluginsVisible => LeftPluginsVisibilityConverter.Instance;
    public static MachineStateToBrushConverter MachineStateBrush => MachineStateToBrushConverter.Instance;
    public static BoolToSignalBrushConverter SignalBrush => BoolToSignalBrushConverter.Instance;
    public static IsNotNullConverter IsNotNull => IsNotNullConverter.Instance;
    public static IsFlyoutModuleConverter IsFlyout => IsFlyoutModuleConverter.Instance;
    public static EnumToBoolConverter EnumToBool => EnumToBoolConverter.Instance;
    public static NumericTextConverter NumericText => NumericTextConverter.Instance;
    public static BoolNotConverter BoolNot => BoolNotConverter.Instance;
    public static EnumAndBoolConverter EnumAndBool => EnumAndBoolConverter.Instance;
    public static ZDotMarginConverter ZDotMargin => ZDotMarginConverter.Instance;
}

/// <summary>MultiBinding: value[0] is an enum equal to the ConverterParameter
/// AND value[1] is true. Used for the edge finder's Z-probe marker — the red
/// dot shows on the selected edge/corner only when Probe Z is on (ioSender's
/// EnumValueToVisibleConverter, bool flavor).</summary>
public sealed class EnumAndBoolConverter : IMultiValueConverter
{
    public static readonly EnumAndBoolConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // MultiBinding feeds the converter intermediate/error values (e.g.
        // BindingNotification) while child bindings start up, before the
        // DataContext arrives — those must not reach Enum.Parse.
        if (values.Count < 2 || values[0] is not Enum probed || parameter is null)
            return false;
        var target = Enum.Parse(probed.GetType(), parameter.ToString()!, ignoreCase: true);
        return probed.Equals(target) && values[1] is true;
    }
}

/// <summary>Positions the edge finder's Z-probe marker (red dot) within the
/// selected cell. External probing sits it on the workpiece near the corner /
/// edge; internal probing mirrors it OUTSIDE the cell onto the pocket wall —
/// the dot must stay on material, not float over the pocket opening. Margins
/// are ioSender's exact values (35 in from the cell corner externally, -43
/// beyond it internally).</summary>
public sealed class ZDotMarginConverter : IValueConverter
{
    public static readonly ZDotMarginConverter Instance = new();

    private static readonly Dictionary<string, Thickness> External = new()
    {
        ["D"] = new(35, 35, 0, 0),
        ["CD"] = new(0, 35, 0, 0),
        ["C"] = new(0, 35, 35, 0),
        ["AD"] = new(35, 0, 0, 0),
        ["Z"] = new(0),
        ["CB"] = new(0, 0, 35, 0),
        ["A"] = new(35, 0, 0, 35),
        ["AB"] = new(0, 0, 0, 35),
        ["B"] = new(0, 0, 35, 35),
    };

    private static readonly Dictionary<string, Thickness> Internal = new()
    {
        ["D"] = new(-43, -43, 0, 0),
        ["CD"] = new(0, -43, 0, 0),
        ["C"] = new(0, -43, -43, 0),
        ["AD"] = new(-43, 0, 0, 0),
        ["Z"] = new(0),
        ["CB"] = new(0, 0, -43, 0),
        ["A"] = new(-43, 0, 0, -43),
        ["AB"] = new(0, 0, 0, -43),
        ["B"] = new(0, 0, -43, -43),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // value is the SelectedTool enum; guard the startup BindingNotification.
        if (value is not ProbingType tool || parameter is null)
            return new Thickness(0);
        var table = tool == ProbingType.EdgeFinderInternal ? Internal : External;
        return table.TryGetValue(parameter.ToString()!, out var margin) ? margin : new Thickness(0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a bool; used to show one of two stacked pictures (e.g. the
/// rotation AB pictogram with/without the extra probe point).</summary>
public sealed class BoolNotConverter : IValueConverter
{
    public static readonly BoolNotConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => global::Avalonia.Data.BindingOperations.DoNothing;
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

/// <summary>
/// Compares an enum property with a ConverterParameter (the enum member name);
/// used to drive RadioButton/toggle selection from a single enum property.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;
        var target = Enum.Parse(value.GetType(), parameter.ToString()!, ignoreCase: true);
        return value.Equals(target);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
            return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
        return global::Avalonia.Data.BindingOperations.DoNothing;
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

public sealed class MachineStateToBrushConverter : IValueConverter
{
    public static readonly MachineStateToBrushConverter Instance = new();

    private static readonly IBrush RunBrush = new SolidColorBrush(Color.Parse("#c8e8c8"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#ffe8a0"));
    private static readonly IBrush AlarmBrush = new SolidColorBrush(Color.Parse("#e04545"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#e0e0e0"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? string.Empty;
        if (s.Contains("run", StringComparison.OrdinalIgnoreCase)) return RunBrush;
        if (s.Contains("alarm", StringComparison.OrdinalIgnoreCase)) return AlarmBrush;
        if (s.Contains("idle", StringComparison.OrdinalIgnoreCase)) return IdleBrush;
        // hold, check, door, etc. -> idle yellow; unknown -> gray
        if (s.Contains("hold", StringComparison.OrdinalIgnoreCase)) return IdleBrush;
        if (s.Contains("check", StringComparison.OrdinalIgnoreCase)) return IdleBrush;
        if (string.IsNullOrWhiteSpace(s)) return IdleBrush;
        return DefaultBrush;
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

public sealed class IsNotNullConverter : IValueConverter
{
    public static readonly IsNotNullConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;
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
