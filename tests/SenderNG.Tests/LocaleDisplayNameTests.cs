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
using SenderNG.Localization;
using Xunit;

namespace SenderNG.Tests;

public class LocaleDisplayNameTests
{
    [Theory]
    [InlineData("en", "English")]
    [InlineData("ru", "Русский")]
    [InlineData("fr", "Français")]
    [InlineData("de", "Deutsch")]
    [InlineData("es", "Español")]
    public void Known_codes_resolve_to_their_native_name(string code, string expected) =>
        Assert.Equal(expected, LocaleDisplayNames.For(code));

    [Fact]
    public void Unknown_codes_pass_through_unchanged()
    {
        // Plugin-contributed locales (AvailableLocales merges module codes)
        // have no entry here and must remain selectable and displayable.
        Assert.Equal("pl", LocaleDisplayNames.For("pl"));
        Assert.Equal("PL", LocaleDisplayNames.For("PL"));
    }

    [Fact]
    public void The_converter_maps_strings_and_passes_other_values_through()
    {
        // Fully qualified: inside namespace SenderNG.Tests the simple name
        // "Converters" binds to the enclosing namespace, not the static class.
        var converter = SenderNG.Converters.Converters.LocaleDisplayName;
        Assert.Equal("Deutsch", converter.Convert("de", typeof(string), null, CultureInfo.CurrentCulture));
        Assert.Equal(42, converter.Convert(42, typeof(object), null, CultureInfo.CurrentCulture));
    }
}
