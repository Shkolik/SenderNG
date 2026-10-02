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

using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using CNC.Core.Services;
using SenderNG;
using SenderNG.Core.Localization;
using Xunit;

namespace SenderNG.Tests;

/// <summary>
/// Pins the four settings-fallback notices the host composes, in both shipped
/// locales. The bug class here is a message that states something untrue, so each
/// assertion covers what the text must NOT say as well as what it must.
/// </summary>
public class SettingsFallbackMessageTests
{
    // A private instance (not HeadlessTestCatalog.Locale): SetLocale calls here must
    // never race other test classes that read the shared static catalog.
    //
    // The class is [AvaloniaTheory]/[AvaloniaFact] rather than plain [Theory] for the
    // same reason, one level up. LocalizationService.Current is process-global and
    // Notice() has to write it, so these tests are only safe if they cannot run
    // concurrently with AboutDialogModelTests, which reads the same static. The
    // Avalonia attributes put every class that touches the catalog into the one
    // serialized collection; plain [Fact] classes each get their own and run in
    // parallel. Measured: as plain [Theory], this class failed
    // AboutDialogModelTests.BuildCopyText_..._active_language on 4 of 4 runs.
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private static readonly string[] FallbackKeys =
    [
        "app.msg.settingsCorruptMoved",
        "app.msg.settingsCorruptKept",
        "app.msg.settingsUnreadableMoved",
        "app.msg.settingsUnreadableKept",
    ];

    private static string Describe(SettingsFallback fallback)
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            return (string)typeof(HostSetup)
                .GetMethod("DescribeSettingsFallback", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { fallback })!;
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    private static SettingsFallback Fallback(SettingsFallbackReason reason, bool quarantined) =>
        new(@"C:\s\global.json", reason, null) { Quarantined = quarantined };

    /// <summary>The message a user of <paramref name="locale"/> actually sees: the
    /// locale file's own text, not the English literal the C# side falls back to.</summary>
    private static string Notice(SettingsFallbackReason reason, bool quarantined, string locale)
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            Loc.SetLocale(locale);
            return Describe(Fallback(reason, quarantined));
        }
        finally
        {
            Loc.SetLocale("en");
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("ru")]
    public void A_quarantined_corrupt_file_says_where_the_original_went(string locale)
    {
        var message = Notice(SettingsFallbackReason.Corrupt, true, locale);

        Assert.Contains("global.json", message);
        Assert.Contains(".corrupt", message);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("ru")]
    public void A_corrupt_file_that_stayed_in_place_promises_no_copy(string locale)
    {
        var message = Notice(SettingsFallbackReason.Corrupt, false, locale);

        Assert.Contains("global.json", message);
        // Quarantine failed, so there is no .corrupt copy anywhere. Saying one was
        // made sends the user looking for a file that does not exist.
        Assert.DoesNotContain(".corrupt", message);
        Assert.DoesNotContain("moved", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("saved as", message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("en", "damaged")]
    [InlineData("ru", "поврежд")]
    public void An_unreadable_file_is_never_reported_as_damaged(string locale, string damageWord)
    {
        // The regression pin: a locked or permission-denied file has fine bytes.
        // "damaged" sends the user hunting for corruption that does not exist, and
        // Unreadable usually arrives with Quarantined == false, so this was the
        // likely case rather than the edge one.
        var message = Notice(SettingsFallbackReason.Unreadable, false, locale);

        Assert.Contains("global.json", message);
        Assert.DoesNotContain(damageWord, message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("en", "moved", "damaged")]
    [InlineData("ru", "перемещ", "поврежд")]
    public void An_unreadable_file_that_was_moved_aside_still_says_so(
        string locale, string movedWord, string damageWord)
    {
        var message = Notice(SettingsFallbackReason.Unreadable, true, locale);

        Assert.Contains("global.json", message);
        Assert.Contains(".corrupt", message);
        Assert.Contains(movedWord, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(damageWord, message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("en", true, "damaged")]
    [InlineData("en", false, "damaged")]
    [InlineData("ru", true, "поврежд")]
    [InlineData("ru", false, "поврежд")]
    public void A_corrupt_file_is_always_reported_as_damaged(
        string locale, bool quarantined, string damageWord)
    {
        // The other direction of the same lie: the corruption claim must not be
        // quietly stripped by a translator, since the user's file really is bad.
        var message = Notice(SettingsFallbackReason.Corrupt, quarantined, locale);

        Assert.Contains("global.json", message);
        Assert.Contains(damageWord, message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("ru")]
    public void Every_fallback_key_exists_in_every_locale_with_a_usable_placeholder(string locale)
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Locale", locale + ".json")));
        var strings = doc.RootElement.GetProperty("strings");

        foreach (var key in FallbackKeys)
        {
            Assert.True(strings.TryGetProperty(key, out var value), $"{locale}.json is missing {key}");
            var text = value.GetString() ?? string.Empty;
            Assert.Contains("{0}", text);
            // A malformed format string (a stray brace, a named placeholder) throws
            // here; a lost {0} fails the Contains above.
            Assert.Contains("sample", string.Format(text, "sample"));
        }
    }

    [AvaloniaFact]
    public void Each_of_the_four_cells_resolves_to_its_own_message()
    {
        // Guards the switch itself: two cells collapsing onto one key would leave
        // every assertion above green while one case went unreported.
        var messages = new[]
        {
            Notice(SettingsFallbackReason.Corrupt, true, "en"),
            Notice(SettingsFallbackReason.Corrupt, false, "en"),
            Notice(SettingsFallbackReason.Unreadable, true, "en"),
            Notice(SettingsFallbackReason.Unreadable, false, "en"),
        };

        Assert.Equal(messages.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }
}