/*
 * PlotterLocaleTests.cs
 *
 * Drift guard for the Skia plotter's own locale files: en and ru must carry the
 * same keys, every key the view chrome resolves must exist in both, none may be
 * blank, and the orphaned `module.plotter.loft` must stay deleted.
 *
 * Two corrections to the plan's version of this file, both verified against the
 * files rather than assumed:
 *
 * 1. The keys live under a root-level "strings" object, so every lookup has to
 *    descend into it. The plan's helper enumerated the ROOT, which yields the
 *    single property "strings" - so its "both files carry the same keys" test
 *    would have passed vacuously and "every view chrome key resolves" would have
 *    failed for the wrong reason (a missing key, not a missing file).
 * 2. Neither file carries a UTF-8 BOM (first bytes are 7B 0D 0A 20 - `{`,
 *    CR, LF, space), contrary to the plan's note. ReadAllText works either way
 *    and detects a BOM if one is ever added, so the call stays.
 */

using System.Text;
using System.Text.Json;

namespace SenderNG.Core.Tests.Localization;

public class PlotterLocaleTests
{
    /// <summary>The 12 keys the view chrome resolves: 11 label properties on
    /// <c>PlotterSettingsViewModel</c> plus the per-row plane placeholder.
    /// Listed here rather than reflected out of the VM so that deleting a label
    /// from the VM without adding its key still fails the build.</summary>
    private static readonly string[] ViewChromeKeys =
    [
        "module.plotter.settings.view",
        "module.plotter.settings.showGrid",
        "module.plotter.settings.showOrigin",
        "module.plotter.settings.showEnvelope",
        "module.plotter.settings.showRunState",
        "module.plotter.settings.worldAxes",
        "module.plotter.settings.origin",
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
        foreach (var key in ViewChromeKeys)
        {
            Assert.True(Keys("en.json").Contains(key), $"en.json is missing {key}");
            Assert.True(Keys("ru.json").Contains(key), $"ru.json is missing {key}");
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
        Assert.Contains("{0}", Strings(locale).GetProperty("module.plotter.settings.plane").GetString());
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

    private static HashSet<string> Keys(string file) =>
        Strings(file).EnumerateObject().Select(p => p.Name).ToHashSet();

    private static JsonElement Strings(string file) =>
        JsonDocument.Parse(File.ReadAllText(LocalePath(file), Encoding.UTF8))
            .RootElement
            .GetProperty("strings");

    /// <summary>Walks up from the test binary to the repo root, then down into
    /// the plotter module's locale folder. Self-contained, so the guard does
    /// not depend on the working directory the runner happens to pick.</summary>
    private static string LocalePath(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(
                dir.FullName, "src", "SenderNG.Modules.Plotter", "Locale", file);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException($"Could not find src/SenderNG.Modules.Plotter/Locale/{file}.");
    }
}
