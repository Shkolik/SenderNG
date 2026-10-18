/*
 * ThemeVariantsTests.cs - pins the theme-setting to Avalonia-variant mapping
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

using Avalonia;
using Avalonia.Styling;
using SenderNG.Themes;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")] // mutates Application.Current.RequestedThemeVariant
public class ThemeVariantsTests
{
    [Theory]
    [InlineData(null, "Default")]
    [InlineData("", "Default")]
    [InlineData("system", "Default")]
    [InlineData("light", "Light")]
    [InlineData("Light", "Light")]
    [InlineData("dark", "Dark")]
    [InlineData("nonsense", "Default")]
    public void ToVariant_maps_the_three_codes(string? input, string expected)
    {
        var expectedVariant = expected switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        Assert.Equal(expectedVariant, ThemeVariants.ToVariant(input));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Apply_sets_the_application_variant()
    {
        try
        {
            ThemeVariants.Apply("dark");
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            ThemeVariants.Apply("");
            Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
        }
        finally
        {
            ThemeVariants.Apply("system");
        }
    }
}
