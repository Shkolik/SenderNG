/*
 * PlotterGlLocaleTests.cs - guards the plotter-gl module's real Locale
 * catalogs: both files parse, carry no UTF-8 BOM, hold the per-kind
 * render-unavailable hints and settings labels, agree on their key set, and
 * hold no blank value
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
using SenderNG.Modules.Plotter.GL;

namespace SenderNG.Core.Tests;

/// <summary>
/// Guards the plotter-gl module's shipped Locale/*.json, and only those. The
/// other modules staging catalogs into the same output tree (plotter, excellon,
/// hpgl, probing) and the app's own Locale/ are NOT covered here.
/// The name is deliberately narrow rather than a walk over every Modules/*/Locale,
/// because that would reach outside this feature and surface any pre-existing
/// mismatch in another module's files.
/// <para>These two files need guarding because every other check of them is
/// either a one-off shell command or the view model's own degradation path, and
/// both are blind to a mistake in the data:
/// <c>PlotterGlViewModel.RenderFailureDetail</c> resolves a missing hint key to
/// the generic <c>renderUnavailableHint</c> and renders a perfectly plausible
/// panel, so a key typo — or a key added to en.json and forgotten in ru.json —
/// would ship as a silently degraded message rather than a failure.</para>
/// <para>Paths come from the module-staging convention the ScanDirectory tests
/// already rely on: the catalogs are linked into this project's
/// OutputDir/Modules/plotter-gl/Locale by SenderNG.csproj, so the files read
/// here are copies of the sources that actually ship.</para>
/// </summary>
public class PlotterGlLocaleTests
{
    /// <summary>The three per-kind hints RenderFailureDetail looks up, one per
    /// non-None PlotGlFailure member. They are literals in production, inside a
    /// switch expression, so a test cannot reach them without changing
    /// production code for a test's benefit — which means a rename there leaves
    /// this list quietly stale and every assertion below still green.
    /// KEEP IN SYNC with PlotterGlViewModel.RenderFailureDetail
    /// (PlotterGlViewModel.cs).</summary>
    public static TheoryData<string> RenderHintKeys() => new()
    {
        "module.plotter-gl.renderUnavailable.notSupported",
        "module.plotter-gl.renderUnavailable.runtimeError",
        "module.plotter-gl.renderUnavailable.other",
    };

    /// <summary>Settings labels resolved code-side by
    /// <c>PlotterGlSettingsViewModel</c> the same way the hints are: a miss
    /// degrades to the English fallback literal, silently, in every
    /// language.</summary>
    public static TheoryData<string> SettingsLabelKeys() => new()
    {
        "module.plotter-gl.settings.showLoft",
    };

    [Theory]
    [MemberData(nameof(SettingsLabelKeys))]
    public void Every_settings_label_key_exists_in_both_locales(string key)
    {
        foreach (var locale in Locales)
        {
            var strings = Strings(locale);
            Assert.True(strings.ContainsKey(key),
                $"{locale}.json has no '{key}'. Without it the settings label " +
                "falls back to the English literal.");
        }
    }

    [Theory]
    [MemberData(nameof(RenderHintKeys))]
    public void Every_render_unavailable_hint_key_exists_in_both_locales(string key)
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

    /// <summary>The module declaration and the English catalog must agree
    /// exactly: every key <c>PlotterGlModule.LocalizationKeys</c> declares is
    /// in <c>en.json</c>, and <c>en.json</c> has nothing beyond the declared
    /// keys plus <see cref="CatalogOnlyByDesign"/> — no missing, no extra.
    /// <para>Missing-only is what the 2D module's
    /// <c>PlotterModuleTests.Every_declared_locale_key_exists_in_both_catalogs</c>
    /// guards (declared ⊆ catalog), on the reasoning that an extra catalog key
    /// still resolves at runtime. This test also pins the extra direction,
    /// because that is where this module's two known exceptions live and an
    /// unpinned "extra" would otherwise be exactly where a typo'd key hides: a
    /// declared-but-misnamed key falls back to its English literal silently,
    /// and a catalog-only key no test knows about resolves nowhere useful.</para>
    /// <para>The ru direction is covered transitively: the declaration ⊆ en
    /// assertion above plus <c>English_and_russian_key_sets_are_identical</c>
    /// implies declaration ⊆ ru.</para>
    /// <para><see cref="CatalogOnlyByDesign"/> is pinned as a set rather than
    /// exempted by pattern so its members cannot drift unreviewed: removing one
    /// from the catalog fails the "dropped" assertion below, and adding a new
    /// catalog-only key fails the "undeclared" assertion until it is either
    /// declared or consciously added to the pin.</para></summary>
    [Fact]
    public void English_catalog_keys_exactly_match_the_declared_keys_plus_the_pinned_catalog_only_set()
    {
        var declared = new PlotterGlModule().LocalizationKeys.Keys.ToHashSet(StringComparer.Ordinal);
        var catalog = Strings("en").Keys.ToHashSet(StringComparer.Ordinal);

        var missing = declared.Except(catalog).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            "en.json is missing declared key(s): " + string.Join(", ", missing));

        var undeclared = catalog.Except(declared)
            .Except(CatalogOnlyByDesign, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(undeclared.Count == 0,
            "en.json has key(s) that are neither declared nor pinned as catalog-only: " +
            string.Join(", ", undeclared));

        var dropped = CatalogOnlyByDesign.Except(catalog).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(dropped.Count == 0,
            "en.json dropped pinned catalog-only key(s): " + string.Join(", ", dropped));
    }

    /// <summary>Keys that legitimately live only in the catalogs, never in
    /// <c>PlotterGlModule.LocalizationKeys</c>.
    /// <para>The first group is runtime strings the view model and view
    /// resolve straight from the catalog (status lines, the failure hint
    /// chain): Nex declares the same split — its <c>LocalizationKeys</c> holds
    /// only the chrome and settings labels — and the verbatim-copy rule for
    /// this scaffold forbids "improving" it here. Their English fallbacks live
    /// as literals in the VM/view, so a catalog miss degrades instead of
    /// crashing, which is exactly what the hint-chain tests pin.</para>
    /// <para>The last three are the anti-aliasing settings entries Step 1.3
    /// removed from the declaration (the AA UI is gone) while the scripted
    /// catalog copy kept them — ruled harmless and left in place by review
    /// ("No action"), so they are pinned here as the documented orphans they
    /// are. Every key in this list must also exist in en.json; see the test
    /// above.</para></summary>
    private static readonly string[] CatalogOnlyByDesign =
    [
        // Runtime status + failure-hint strings (VM/view resolve these directly).
        "module.plotter-gl.noProgram",
        "module.plotter-gl.noMotion",
        "module.plotter-gl.building",
        "module.plotter-gl.buildFailed",
        "module.plotter-gl.renderUnavailable",
        "module.plotter-gl.renderUnavailableHint",
        "module.plotter-gl.renderUnavailable.notSupported",
        "module.plotter-gl.renderUnavailable.runtimeError",
        "module.plotter-gl.renderUnavailable.other",
        // Script-mandated AA orphans: dropped from LocalizationKeys (Step 1.3),
        // kept in the catalogs by review ruling.
        "module.plotter-gl.settings.antiAliasing",
        "module.plotter-gl.settings.aaMode",
        "module.plotter-gl.settings.aaQuality",
    ];

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
    /// not that it contains nothing else. Every key in the module's catalogs is
    /// required to be a translated sentence, so no key is exempt.
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

    /// <summary>The `vulkaninfo` command name must survive translation verbatim
    /// wherever it appears: it is something the user types, and transliterating
    /// it would send them to a command that does not exist. Both kinds that
    /// mention the command are checked, so adding a third does not slip
    /// through unasserted. (The hint *values* still explain the check in terms
    /// of Vulkan — a documented limitation of the scripted copy; the command
    /// name itself is what this test pins.)</summary>
    [Fact]
    public void Russian_keeps_the_vulkaninfo_command_name_intact()
    {
        const string command = "vulkaninfo --summary";
        var ru = Strings("ru");
        var pinned = new[]
        {
            "module.plotter-gl.renderUnavailable.notSupported",
            "module.plotter-gl.renderUnavailable.runtimeError",
        };

        foreach (var key in pinned)
        {
            Assert.True(ru.ContainsKey(key), $"ru.json key '{key}' is missing.");
            Assert.Contains(command, ru[key]);
        }

        // Nothing else in the catalog may mention a command we have not pinned.
        // Membership is tested on the WHOLE key, not a suffix: an unrelated key
        // that merely ends in ".notSupported" must not be able to slip past as
        // if it were one of the two above.
        foreach (var (key, value) in ru)
        {
            if (pinned.Contains(key, StringComparer.Ordinal))
                continue;
            Assert.False(value.Contains("vulkaninfo", StringComparison.Ordinal),
                $"ru.json key '{key}' mentions vulkaninfo but is not covered by " +
                "this test - add its key to `pinned`.");
        }
    }

    /// <summary>No message may claim 2D plotting is still available. This module
    /// cannot know whether the user has the 2D plotting plugin installed, so
    /// asserting it either way is unsupported - the four hints once ended with
    /// that reassurance. The check is on the CLAIM PHRASE, not on the token
    /// "2D": a legitimate sentence may name 2D (see the note on
    /// Every_russian_value_is_translated_rather_than_copied_from_english), and
    /// only the availability promise is forbidden. Every key in both catalogs
    /// is scanned, so a new hint cannot reintroduce the claim unasserted.
    /// </summary>
    [Theory]
    [InlineData("en", "2D plotting stays available")]
    [InlineData("ru", "2D-построение продолжает работать")]
    public void No_message_claims_2d_plotting_stays_available(string locale, string claim)
    {
        foreach (var (key, value) in Strings(locale))
        {
            Assert.False(value.Contains(claim, StringComparison.Ordinal),
                $"{locale}.json key '{key}' still claims 2D plotting availability: '{value}'. " +
                "This module cannot know whether the 2D plugin is installed.");
        }
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

        var dir = Path.Combine(modules, "plotter-gl", "Locale");
        Assert.True(Directory.Exists(dir), "plotter-gl locale staging missing: " + dir);

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
