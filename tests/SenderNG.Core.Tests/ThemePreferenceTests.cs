/*
 * ThemePreferenceTests.cs - pins the theme-preference vocabulary and persistence
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

using CNC.Core.Services;
using Xunit;

namespace SenderNG.Core.Tests;

public class ThemePreferenceTests
{
    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("  ", "system")]
    [InlineData("system", "system")]
    [InlineData("System", "system")]
    [InlineData("SYSTEM", "system")]
    [InlineData("light", "light")]
    [InlineData("Light", "light")]
    [InlineData("dark", "dark")]
    [InlineData("DARK", "dark")]
    [InlineData("rainbow", "system")]
    public void Normalize_maps_to_canonical_lowercase(string? input, string expected) =>
        Assert.Equal(expected, ThemePreference.Normalize(input));

    [Fact]
    public void Available_themes_are_the_three_canonical_codes() =>
        Assert.Equal(new[] { "system", "light", "dark" }, ThemePreference.AvailableThemes);

    [Fact]
    public void Theme_setting_round_trips_through_global_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new SettingsService(dir);
            service.Global.Theme = ThemePreference.Dark;
            service.SaveGlobal(service.Global);

            var reloaded = new SettingsService(dir);
            Assert.Equal("dark", reloaded.Global.Theme);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
