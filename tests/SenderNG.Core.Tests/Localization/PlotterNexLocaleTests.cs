/*
 * PlotterNexLocaleTests.cs - guards the plotter-nex module's real Locale
 * catalogs: both files parse, carry no UTF-8 BOM, hold the per-kind
 * Vulkan-unavailable hints, agree on their key set, and hold no blank value
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

using System.Text.Json;

namespace SenderNG.Core.Tests;

/// <summary>
/// Guards the plotter-nex module's shipped Locale/*.json, and only those. The
/// other modules staging catalogs into the same output tree (plotter, excellon,
/// hpgl, probing) and the app's own Locale/ are NOT covered here. The name is
/// deliberately narrow rather than a walk over every Modules/*/Locale, because
/// that would reach outside this feature and surface any pre-existing mismatch
/// in another module's files.
/// <para>These two files need guarding because every other check of them is
/// either a one-off shell command or the view model's own degradation path, and
/// both are blind to a mistake in the data:
/// <c>PlotterNexViewModel.VulkanFailureDetail</c> resolves a missing hint key to
/// the generic <c>vulkanUnavailableHint</c> and renders a perfectly plausible
/// panel, so a key typo — or a key added to en.json and forgotten in ru.json —
/// would ship as a silently degraded message rather than a failure.</para>
/// <para>Paths come from the module-staging convention the ScanDirectory tests
/// already rely on: the catalogs are linked into this project's
/// OutputDir/Modules/plotter-nex/Locale by SenderNG.csproj, so the files read
/// here are copies of the sources that actually ship.</para>
/// </summary>
public class PlotterNexLocaleTests
{
    /// <summary>The three per-kind hints VulkanFailureDetail looks up, one per
    /// non-None Plot3DFailure member. They are literals in production, inside a
    /// switch expression, so a test cannot reach them without changing
    /// production code for a test's benefit — which means a rename there leaves
    /// this list quietly stale and every assertion below still green.
    /// KEEP IN SYNC with PlotterNexViewModel.VulkanFailureDetail
    /// (PlotterNexViewModel.cs).</summary>
    public static TheoryData<string> VulkanHintKeys() => new()
    {
        "module.plotter-nex.vulkanUnavailable.notSupported",
        "module.plotter-nex.vulkanUnavailable.runtimeError",
        "module.plotter-nex.vulkanUnavailable.other",
    };

    [Theory]
    [MemberData(nameof(VulkanHintKeys))]
    public void Every_vulkan_unavailable_hint_key_exists_in_both_locales(string key)
    {
        foreach (var locale in Locales)
        {
            var strings = Strings(locale);
            Assert.True(strings.ContainsKey(key),
                $"{locale}.json has no '{key}'. Without it the panel falls back to the " +
                "generic hint and the specific reason is never shown.");
        }
    }

    [Fact]
    public void Both_locale_files_parse_as_json()
    {
        foreach (var locale in Locales)
        {
            // Parse throws on malformed JSON — that throw is the assertion.
            using var doc = JsonDocument.Parse(File.ReadAllText(LocaleFile(locale)));

            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.TryGetProperty("strings", out var strings),
                $"{locale}.json has no top-level \"strings\" object.");
            Assert.Equal(JsonValueKind.Object, strings.ValueKind);
        }
    }

    [Fact]
    public void Neither_locale_file_starts_with_a_utf8_bom()
    {
        foreach (var locale in Locales)
        {
            var path = LocaleFile(locale);
            // Raw bytes, not the text reader: the parse test above cannot see a
            // BOM because JsonDocument.Parse tolerates one, so this is the only
            // check enforcing the UTF-8-without-BOM format the catalogs require.
            var head = File.ReadAllBytes(path).Take(3).ToArray();
            Assert.False(head is [0xEF, 0xBB, 0xBF],
                $"{locale}.json starts with a UTF-8 BOM (EF BB BF): {path}. " +
                "The catalogs must be UTF-8 without one.");
        }
    }

    [Fact]
    public void English_and_russian_key_sets_are_identical()
    {
        // Both directions matter: a key added to only one file leaves the other
        // locale's users with the generic message.
        var en = Strings("en").Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var ru = Strings("ru").Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.Equal(en, ru);
    }

    [Fact]
    public void No_value_is_empty_or_whitespace()
    {
        foreach (var locale in Locales)
        {
            foreach (var (key, value) in Strings(locale))
                Assert.False(string.IsNullOrWhiteSpace(value),
                    $"{locale}.json key '{key}' is empty or whitespace.");
        }
    }

    /// <summary>Every Russian value is actually Russian, and is not the English
    /// string pasted across.
    /// <para>
    /// The tests above check presence, key parity, and non-blankness — so
    /// copying the English text into ru.json satisfied all of them, which is
    /// exactly the regression that has already happened twice in this module and
    /// been caught by hand both times. No test can judge whether Russian is
    /// <em>good</em>, but it can catch the copy-paste, and that is the failure
    /// with the worst ratio of damage to detection cost: a user sees English in
    /// a Russian UI and nothing anywhere records a defect.
    /// </para>
    /// <para>
    /// Cyrillic is checked rather than "differs from English" alone, because a
    /// value that differs from English only by a stray character would pass a
    /// weaker test while still being untranslated. Latin-script technical terms
    /// inside a Russian sentence are fine and expected - `vulkaninfo --summary`,
    /// `Vulkan 1.3`, `2D` - so the check is that the value *contains* Cyrillic,
    /// not that it contains nothing else. All five values are full sentences, so
    /// none is exempt.
    /// </para></summary>
    [Fact]
    public void Every_russian_value_is_translated_rather_than_copied_from_english()
    {
        var en = Strings("en");
        var ru = Strings("ru");

        foreach (var (key, russian) in ru)
        {
            Assert.NotEqual(en[key], russian);
            Assert.True(
                russian.Any(c => c is >= 'Ѐ' and <= 'ӿ'),
                $"ru.json key '{key}' has no Cyrillic characters: '{russian}'. " +
                "It looks like the English value was copied across.");
        }
    }

    /// <summary>The `vulkaninfo` command name must survive translation verbatim:
    /// it is something the user types, and transliterating it would send them
    /// to a command that does not exist.</summary>
    [Fact]
    public void Russian_keeps_the_vulkaninfo_command_name_intact()
    {
        const string command = "vulkaninfo --summary";
        Assert.Contains(command, Strings("ru")["module.plotter-nex.vulkanUnavailable.runtimeError"]);
    }

    private static readonly string[] Locales = ["en", "ru"];

    /// <summary>Absolute path to one of the module's real catalogs. Asserts
    /// each level exists: a path that silently resolves to nothing would make
    /// every test above pass on missing data, which is the exact failure this
    /// file is meant to catch.</summary>
    private static string LocaleFile(string locale)
    {
        var modules = Path.Combine(AppContext.BaseDirectory, "Modules");
        Assert.True(Directory.Exists(modules),
            "plugin staging missing — rebuild the test project: " + modules);

        var dir = Path.Combine(modules, "plotter-nex", "Locale");
        Assert.True(Directory.Exists(dir), "plotter-nex locale staging missing: " + dir);

        var file = Path.Combine(dir, locale + ".json");
        Assert.True(File.Exists(file), "locale catalog missing: " + file);

        return file;
    }

    /// <summary>The "strings" object of one catalog. Throws if the file is
    /// malformed or shaped differently, so no caller can mistake a wrong shape
    /// for an empty key set.</summary>
    private static Dictionary<string, string> Strings(string locale)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(LocaleFile(locale)));
        if (!doc.RootElement.TryGetProperty("strings", out var strings) ||
            strings.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"{locale}.json has no top-level \"strings\" object: {LocaleFile(locale)}");

        return strings.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
    }
}
