using System.Globalization;
using Avalonia.Data;
using SenderNG.Converters;
using Xunit;

namespace SenderNG.Core.Tests;

public class NumericTextConverterTests
{
    private static readonly NumericTextConverter Converter = NumericTextConverter.Instance;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void ConvertBack_valid_double_returns_value()
    {
        Assert.Equal(5.5, Converter.ConvertBack("5.5", typeof(double), null, Culture));
    }

    [Fact]
    public void ConvertBack_invalid_text_returns_do_nothing_not_error()
    {
        // Letters, a dangling sign, or an empty field must not raise a binding
        // validation error (red border) or an InvalidCastException.
        Assert.Same(BindingOperations.DoNothing, Converter.ConvertBack("abc", typeof(double), null, Culture));
        Assert.Same(BindingOperations.DoNothing, Converter.ConvertBack("-", typeof(double), null, Culture));
        Assert.Same(BindingOperations.DoNothing, Converter.ConvertBack("", typeof(double), null, Culture));
    }

    [Fact]
    public void ConvertBack_int_target_parses_integer_only()
    {
        Assert.Equal(3, Converter.ConvertBack("3", typeof(int), null, Culture));
        Assert.Same(BindingOperations.DoNothing, Converter.ConvertBack("3.5", typeof(int), null, Culture));
    }

    [Fact]
    public void Convert_formats_double_as_0_dot_3_digits()
    {
        Assert.Equal("5.5", Converter.Convert(5.5, typeof(string), null, Culture));
        Assert.Equal("5", Converter.Convert(5.0, typeof(string), null, Culture));
    }
}
