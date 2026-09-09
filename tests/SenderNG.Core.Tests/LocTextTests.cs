
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

using SenderNG.Localization;
using SenderNG.Core.Localization;

namespace SenderNG.Core.Tests;

[Collection("LocalizationState")] // serializes with other tests mutating LocalizationService.Current
public class LocTextTests
{
    private static readonly LocalizationService Locale = TestLocalization.Locale;

    [Fact]
    public void Resolve_returns_english_when_no_service_current()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = null;
            Assert.Equal("Hello", LocText.Resolve("app.hello", "Hello"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Resolve_returns_localized_when_service_current()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            Assert.Equal("GRBL settings", LocText.Resolve("grblSettings.title", "GRBL settings"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Resolve_returns_english_when_key_missing()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            Assert.Equal("Fallback", LocText.Resolve("module.nope.missing", "Fallback"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Format_uses_localized_template_and_arguments()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            // en.json: "module.goto.commandFailed": "{0} failed: {1}"
            Assert.Equal("G1 failed: boom", LocText.Format("module.goto.commandFailed", "{0} failed: {1}", "G1", "boom"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Format_falls_back_to_english_on_format_error()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            // Key missing -> english fallback "{bad" is a malformed format string that
            // throws FormatException, exercising the catch branch.
            Assert.Equal("{bad", LocText.Format("module.nonexistent", "{bad", 5));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }
}