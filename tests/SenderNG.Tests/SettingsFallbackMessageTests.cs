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
using Microsoft.Extensions.DependencyInjection;
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
    // These tests mutate LocalizationService.Current, which is process-global and
    // read by other test classes, so two precautions are load-bearing:
    //
    // 1. A private instance, not HeadlessTestCatalog.Locale, so the SetLocale
    //    calls below cannot leak into the catalog the other classes read.
    // 2. The headless attributes, matching the 7 of the 8 classes in this
    //    project that touch Current. Being honest about the limit: these
    //    attributes carry no [Collection], so xUnit's default
    //    CollectionPerClassTestCollectionFactory still gives each class its own
    //    collection and the default 'collections' parallel mode still applies.
    //    The attributes widen the window in which Current is swapped; they do
    //    not close the race. AboutDialogModelTests is a plain [Fact] class, so a
    //    concurrent run remains possible and is a known pre-existing issue in
    //    this project, not something these attributes solve.
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    // The path the test's SettingsFallback reports, and the file name the composed
    // message names — HostSetup formats the message with Path.GetFileName, so these
    // are deliberately different strings and assertions on the message must use this
    // one. The `.corrupt` checks below depend on that split: a quoted path in the
    // message is the literal file name plus suffix, never the directory too.
    private const string path = @"C:\s\global.json";
    private const string fileName = "global.json";

    private static readonly string[] FallbackKeys =
    [
        "app.msg.settingsCorruptMoved",
        "app.msg.settingsCorruptKept",
        "app.msg.settingsUnreadableMoved",
        "app.msg.settingsUnreadableKept",
    ];

    // Reached by reflection: the message is composed in the host, not in a
    // place the test project can call directly.
    private static string Describe(SettingsFallback fallback) =>
        (string)typeof(HostSetup)
            .GetMethod("DescribeSettingsFallback", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { fallback })!;

    private static SettingsFallback Fallback(SettingsFallbackReason reason, bool quarantined) =>
        new(path, reason, null) { Quarantined = quarantined };

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
    [InlineData("en", "kept")]
    [InlineData("ru", "сохранён")]
    public void A_quarantined_corrupt_file_says_where_the_original_went(string locale, string keptWord)
    {
        var message = Notice(SettingsFallbackReason.Corrupt, true, locale);

        Assert.Contains(fileName, message);
        Assert.Contains(".corrupt", message);
        // The positive half: the whole point of this cell is that the user's
        // original is preserved, so "it may be somewhere near a .corrupt file"
        // must not pass as an adequate description.
        Assert.Contains(keptWord, message, StringComparison.OrdinalIgnoreCase);
        // Not named: QuarantinePath returns .corrupt, .corrupt.1, .corrupt.2 … and
        // SettingsFallback carries no quarantine name. Naming the literal path
        // would point the user at global.json.corrupt, which in the collision
        // case exists and holds an older, different bad file.
        Assert.DoesNotContain($"{fileName}.corrupt", message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("en", "unchanged")]
    [InlineData("ru", "не изменён")]
    public void A_corrupt_file_that_stayed_in_place_promises_no_copy(string locale, string unchangedWord)
    {
        var message = Notice(SettingsFallbackReason.Corrupt, false, locale);

        Assert.Contains(fileName, message);
        // Quarantine failed, so there is no .corrupt copy anywhere. Saying one was
        // made sends the user looking for a file that does not exist.
        Assert.DoesNotContain(".corrupt", message);
        Assert.DoesNotContain("moved", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("saved as", message, StringComparison.OrdinalIgnoreCase);
        // The positive half, and the reason this cell needs its own key: a message
        // saying only "was damaged" leaves the user with no idea whether the file
        // is still there. "It is unchanged" is the whole content of this branch.
        Assert.Contains(unchangedWord, message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("en", "damaged", "unchanged", "permission")]
    [InlineData("ru", "поврежд", "не изменён", "прав")]
    public void An_unreadable_file_is_never_reported_as_damaged(
        string locale, string damageWord, string unchangedWord, string permissionWord)
    {
        // The regression pin: a locked or permission-denied file has fine bytes.
        // "damaged" sends the user hunting for corruption that does not exist. A
        // directory squatting the settings path demonstrates Unreadable arriving
        // with Quarantined == false (see SettingsFallbackTests); how often that
        // pair occurs in the wild is not measured, and the four-cell split does
        // not depend on it.
        var message = Notice(SettingsFallbackReason.Unreadable, false, locale);

        Assert.Contains(fileName, message);
        Assert.DoesNotContain(damageWord, message, StringComparison.OrdinalIgnoreCase);
        // The two positive claims this cell owes the user. Without them a message
        // saying only "could not be read" is a complete sentence and still tells
        // the user nothing about the file or what to do about it.
        Assert.Contains(unchangedWord, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(permissionWord, message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("en", "moved", "damaged", "permission")]
    [InlineData("ru", "перемещ", "поврежд", "прав")]
    public void An_unreadable_file_that_was_moved_aside_still_says_so(
        string locale, string movedWord, string damageWord, string permissionWord)
    {
        var message = Notice(SettingsFallbackReason.Unreadable, true, locale);

        Assert.Contains(fileName, message);
        Assert.Contains(".corrupt", message);
        Assert.Contains(movedWord, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(damageWord, message, StringComparison.OrdinalIgnoreCase);
        // Reading a file you cannot move is itself a permissions problem, so the
        // advice has to survive into the moved cell too. Dropping it here was
        // what let a bad translation turn this branch into a bare "moved".
        Assert.Contains(permissionWord, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{fileName}.corrupt", message, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains(fileName, message);
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

    [AvaloniaTheory]
    [InlineData("en", "reset")]
    [InlineData("ru", "сброшен")]
    public void No_notice_claims_the_users_file_itself_was_reset(
        string locale, string resetWord)
    {
        // The file on disk is not reset by anything here: the two corrupt cells
        // rename it to *.corrupt and the two unreadable cells leave it alone, and
        // the in-memory defaults are a separate thing from the file. Saying the
        // file "was reset" tells the user their customizations are gone when
        // global.json is sitting right there, byte for byte, renamed.
        var cells = new[]
        {
            (SettingsFallbackReason.Corrupt, true),
            (SettingsFallbackReason.Corrupt, false),
            (SettingsFallbackReason.Unreadable, true),
            (SettingsFallbackReason.Unreadable, false),
        };

        foreach (var (reason, quarantined) in cells)
        {
            var message = Notice(reason, quarantined, locale);
            Assert.DoesNotContain(resetWord, message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [AvaloniaFact]
    public void The_english_fallback_survives_a_missing_locale_service()
    {
        // A file-based LocalizationService that has not been loaded, or a trimmed
        // app where it failed to load, leaves Current null. The message must
        // still name the file, and it must not start guessing at why: this path
        // has not decided between damaged, unreadable and quarantined, so the
        // honest text is the one that claims nothing.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = null!;
            var message = Describe(Fallback(SettingsFallbackReason.Corrupt, false));

            Assert.Contains(fileName, message);
            Assert.DoesNotContain("damaged", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("moved", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".corrupt", message);
            Assert.DoesNotContain("reset", message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public void A_reason_nobody_wrote_a_sentence_for_throws_instead_of_showing_vague_text()
    {
        // The explicit throw arm: if a third reason is ever added to the enum
        // without a message, this must fail loudly at the switch rather than
        // quietly show the user something that is merely unhelpful. Reflection
        // wraps it, hence the unwrap.
        var unknown = (SettingsFallbackReason)99;
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var ex = Assert.Throws<TargetInvocationException>(() =>
                Describe(Fallback(unknown, false)));

            Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public void The_host_hands_settings_service_a_live_provider_so_profile_cleanup_still_runs()
    {
        // The regression pin for the registration itself, not for a message.
        // Routing the fallback notice needed a factory; the factory it replaced
        // was AddSingleton<SettingsService>(), and MS DI injects an
        // IServiceProvider into that ctor parameter on its own — so before the
        // factory, SettingsService._services was a live provider and DeleteProfile
        // resolved IProfileCompanionSuffixes through it. A factory that forgets
        // `services: sp` leaves it null, and _services?.GetServices(...) then
        // silently yields nothing: every deleted profile keeps its per-module
        // companion files and no test in the project fails.
        //
        // Built through the real BuildHost, so this pins the production
        // registration rather than a hand-rolled replica of it that could drift.
        using var host = HostSetup.BuildHost([]);

        var settings = host.Services.GetRequiredService<SettingsService>();
        var injected = (IServiceProvider?)typeof(SettingsService)
            .GetField("_services", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(settings);

        // Not Assert.Same against host.Services: the factory is handed the root
        // engine scope, which is a different object that wraps the same
        // registrations, and identity here would pin an MS DI implementation
        // detail rather than the behaviour. Liveness is the claim that matters.
        Assert.NotNull(injected);
        // And the provider really does carry companion providers, so a null one
        // cost something concrete rather than nothing. Probing ships
        // IProfileCompanionSuffixes, so the regression had a real victim.
        Assert.NotEmpty(injected!.GetServices<SenderNG.Core.Services.IProfileCompanionSuffixes>());
    }
}