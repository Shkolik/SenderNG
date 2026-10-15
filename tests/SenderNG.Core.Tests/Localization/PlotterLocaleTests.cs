/*
 * PlotterLocaleTests.cs
 *
 * Drift guard for the Skia plotter's own locale files: en and ru must carry the
 * same keys, every key the view chrome resolves must exist in both, none may be
 * blank, the one formatted key must keep its placeholder, and the orphaned
 * `module.plotter.loft` and `module.plotter.isometric` must stay deleted.
 *
 * Three corrections to the plan's version of this file, each verified against the
 * files rather than assumed:
 *
 * 1. The keys live under a root-level "strings" object, so every lookup has to
 *    descend into it. The plan's helper enumerated the ROOT, which yields the
 *    single property "strings" - so its "both files carry the same keys" test
 *    would have compared {"strings"} with {"strings"} and passed vacuously, and
 *    its other two tests would have failed for the wrong reason.
 * 2. The plan's note that "the locale files carry a UTF-8 BOM and
 *    JsonDocument.Parse on raw bytes rejects it" has the second half right for
 *    the wrong reason: these files carry NO BOM (first bytes 7B 0D 0A 20), and
 *    the BOM only matters because the APP parses module locales from raw bytes
 *    (LocalizationService.ReadStrings, called unguarded from
 *    RegisterModuleLocales). Measured, not assumed:
 *      JsonDocument.Parse(<BOM>{"strings":{}})  ->  JsonReaderException
 *          '0xEF' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.
 *    File.ReadAllText(path, Encoding.UTF8) STRIPS a BOM, so a text-based guard
 *    cannot see one - hence The_catalogs_carry_no_utf8_bom below, which reads the
 *    bytes.
 * 3. No locale guard existed anywhere before this file, so the walk-up here
 *    is self-contained — it copies from no other locale test.
 *
 * SOURCE tree, not the staged copy, is what these checks read: a mutation run
 * edits the file and re-runs the test without a rebuild, and a CopyToOutputDirectory
 * staging step would quietly hide the mutation behind a stale copy. The staged
 * copy is asserted separately, by The_staged_catalogs_ship_the_keys - because that
 * is the one the app actually parses, and a csproj that stopped staging it would
 * leave every label on its English fallback with nothing else noticing.
 */

using System.Text;
using System.Text.Json;

namespace SenderNG.Core.Tests.Localization;

public class PlotterLocaleTests
{
    /// <summary>The 10 keys the view chrome still resolves: 9 label properties on
    /// <c>PlotterSettingsViewModel</c> plus the per-row plane placeholder. The
    /// per-plane world-axes / origin rows went with the per-plotter axis mapping
    /// (2026-10-14 — the machine profile owns it now), so their two keys are gone
    /// from here and from the catalog.
    /// Listed here rather than reflected out of the VM so that deleting a label
    /// from the VM without adding its key still fails the build.</summary>
    private static readonly string[] ViewChromeKeys =
    [
        "module.plotter.settings.view",
        "module.plotter.settings.showGrid",
        "module.plotter.settings.showOrigin",
        "module.plotter.settings.showEnvelope",
        "module.plotter.settings.showRunState",
        "module.plotter.settings.plane",
        "module.plotter.zoomIn",
        "module.plotter.zoomOut",
        "module.plotter.zoomFit",
        "module.plotter.savePng",
    ];

    [Fact]
    public void English_and_russian_carry_the_same_keys()
    {
        var en = Keys("en.json");
        var ru = Keys("ru.json");
        Assert.Equal(
            en.OrderBy(k => k, StringComparer.Ordinal),
            ru.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_view_chrome_key_resolves_in_both_locales()
    {
        // Read once per file, not once per key: the plan's shape re-parsed both
        // files inside the loop, 24 reads and 24 JsonDocuments for 2 files.
        var en = Keys("en.json");
        var ru = Keys("ru.json");
        foreach (var key in ViewChromeKeys)
        {
            Assert.True(en.Contains(key), $"en.json is missing {key}");
            Assert.True(ru.Contains(key), $"ru.json is missing {key}");
        }
    }

    [Fact]
    public void No_view_chrome_key_resolves_to_an_empty_string()
    {
        foreach (var locale in new[] { "en.json", "ru.json" })
        {
            var strings = Strings(locale);
            foreach (var key in ViewChromeKeys)
            {
                Assert.True(strings.TryGetProperty(key, out var value), $"{locale}:{key} is absent");
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{locale}:{key} is empty");
            }
        }
    }

    /// <summary>The plane row substitutes a 1-based number, so the placeholder has
    /// to survive translation — a Russian "Плоскость" with no <c>{0}</c> would
    /// render every row identically.</summary>
    [Theory]
    [InlineData("en.json")]
    [InlineData("ru.json")]
    public void The_plane_key_keeps_its_placeholder(string locale)
    {
        // TryGetProperty rather than GetProperty: a bare KeyNotFoundException names
        // neither the file nor the key, so an absent key and a lost placeholder
        // produced indistinguishable failures.
        var strings = Strings(locale);
        Assert.True(
            strings.TryGetProperty("module.plotter.settings.plane", out var plane),
            $"{locale}:module.plotter.settings.plane is absent");
        Assert.Contains("{0}", plane.GetString());
    }

    [Fact]
    public void The_orphaned_loft_key_is_gone()
    {
        // Deleted with the `module.plotter.loft` flag in Task 9, along with its
        // entry in the code fallback dictionary. Asserted here so a re-add of
        // either has to be a deliberate edit to this test too.
        Assert.DoesNotContain("module.plotter.loft", Keys("en.json"));
        Assert.DoesNotContain("module.plotter.loft", Keys("ru.json"));
    }

    /// <summary>The iso label moved into the [2D]/[Iso] ToggleButtons as a
    /// literal when the checkbox was replaced, which left this key with no
    /// reader. Asserted gone so a re-add has to be a deliberate edit here too,
    /// exactly as the loft key was.</summary>
    [Fact]
    public void The_orphaned_isometric_key_is_gone()
    {
        Assert.DoesNotContain("module.plotter.isometric", Keys("en.json"));
        Assert.DoesNotContain("module.plotter.isometric", Keys("ru.json"));
    }

    /// <summary>A BOM here is a startup failure, not a style note: the app parses
    /// module locales from raw bytes (<c>LocalizationService.ReadStrings</c>, via
    /// <c>RegisterModuleLocales</c>), and a BOM makes that throw. Nothing else in
    /// the suite would see it, because every label in these files also has a
    /// hardcoded English fallback.</summary>
    [Theory]
    [InlineData("en.json")]
    [InlineData("ru.json")]
    public void The_catalogs_carry_no_utf8_bom(string locale)
    {
        var bytes = File.ReadAllBytes(LocalePath(locale));
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{locale} starts with a UTF-8 BOM, which JsonDocument.Parse(bytes) rejects");
    }

    /// <summary>The staged copy is the one the app reads: <c>SenderNG.csproj</c>
    /// stages <c>src/SenderNG.Modules.Plotter/Locale</c> to
    /// <c>Modules/plotter/Locale</c> and <c>HostSetup</c> registers module
    /// locales from there. This test project references that app project, so the
    /// pair flows into its own output through the app's own staging item. There is
    /// deliberately no second <c>Content</c> item here: a duplicate would keep the
    /// file present after the app's staging broke, which is the exact regression
    /// this test exists to catch. Parsed from raw bytes on purpose - that is how
    /// <c>LocalizationService</c> reads it, so a staged BOM fails here too.</summary>
    [Theory]
    [InlineData("en.json")]
    [InlineData("ru.json")]
    public void The_staged_catalogs_ship_the_keys(string locale)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Modules", "plotter", "Locale", locale);
        Assert.True(
            File.Exists(path),
            $"SenderNG.csproj is expected to stage this file to Modules/plotter/Locale/{locale}, but the test "
            + "output has no copy - the app's module-locale staging item is missing or its Link path changed");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var strings = doc.RootElement.GetProperty("strings");
        foreach (var key in ViewChromeKeys)
            Assert.True(strings.TryGetProperty(key, out _), $"staged {locale} is missing {key}");
    }

    private static HashSet<string> Keys(string file) =>
        Strings(file).EnumerateObject().Select(p => p.Name).ToHashSet();

    private static JsonElement Strings(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(LocalePath(file), Encoding.UTF8));
        // Clone() is load-bearing, not decoration: a JsonElement from a parsed
        // document is a view into that document's pooled buffers, so the `using`
        // above would dispose them and leave every caller holding an element that
        // throws ObjectDisposedException on first use. Clone() copies into a
        // detached element that owns its own memory. Measured: deleting this call
        // breaks 6 of the 10 tests below, including the ones that never call
        // EnumerateObject(), which is why this comment must not credit them.
        return doc.RootElement.GetProperty("strings").Clone();
    }

    /// <summary>Walks up from the test binary to the repo root, then down into
    /// the plotter module's locale folder. Self-contained, so the guard does
    /// not depend on the working directory the runner happens to pick. The walk
    /// stops at the checkout's own root - <c>.git</c> is a directory in a normal
    /// clone and a file in a worktree - so a mislaid test project can never make
    /// this read a DIFFERENT checkout's catalogs and pass against them.
    ///
    /// <para><c>internal</c> and used by <c>PlotterModuleTests</c> too: this worktree
    /// sits UNDER the main checkout and both contain the same relative path, so a
    /// second, near-identical walk-up that omitted the <c>.git</c> stop would happily
    /// validate the OUTER checkout's catalogs instead. One walk, one set of rules.</para></summary>
    internal static string LocalePath(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(
                dir.FullName, "src", "SenderNG.Modules.Plotter", "Locale", file);
            if (File.Exists(path))
                return path;
            if (File.Exists(Path.Combine(dir.FullName, ".git")) || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                break;
        }
        throw new FileNotFoundException(
            $"Could not find src/SenderNG.Modules.Plotter/Locale/{file} at or below the checkout root.");
    }
}
