using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SenderNG.Avalonia.Converters;

public static class Converters
{
    public static HasElementsConverter HasElements => HasElementsConverter.Instance;
    public static LeftPluginsVisibilityConverter LeftPluginsVisible => LeftPluginsVisibilityConverter.Instance;
    public static MachineStateToBrushConverter MachineStateBrush => MachineStateToBrushConverter.Instance;
    public static BoolToSignalBrushConverter SignalBrush => BoolToSignalBrushConverter.Instance;
    public static IsNotNullConverter IsNotNull => IsNotNullConverter.Instance;
    public static IsFlyoutModuleConverter IsFlyout => IsFlyoutModuleConverter.Instance;
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
