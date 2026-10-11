/*
 * Converters.cs - numeric text converter for the linked converter plugin views
 */

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

using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace SenderNG.Modules.Plotter.Nex.Converters;

public static class Converters
{
    public static NumericTextConverter NumericText => NumericTextConverter.Instance;
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
