/*
 * BackupRestoreLocaleKeysTests.cs
 *
 * Call-site arity guard for the settings backup & restore feature's own keys.
 *
 * Why it exists: LocaleParityTests only checks that en's placeholders survive
 * translation, and LocKeySourceTests only checks that a key exists at all.
 * Neither notices a catalog value that declares FEWER placeholders than its
 * call site passes — string.Format silently ignores the surplus arguments — so
 * `module.grbl-settings.restoreVerifyMismatch` dropped the list of setting ids
 * that did not take the restored value with the whole suite green (review
 * finding F8). This guard pins, per key, the exact placeholder set the call
 * sites pass, and checks it in all five catalogs.
 *
 * It is deliberately scoped to the keys whose arity the review found unguarded
 * rather than being a general LocText.Format checker: a real checker would need
 * a C# parser (argument counting, splices, ternaries) and would be far more
 * brittle than this list. Adding a key is one line in FeatureKeys.
 *
 * Reads the SOURCE tree, not the staged output copy, for the same reason the
 * other two guards do: a mutation run edits the file and re-runs without a
 * rebuild, and a staged copy would hide the mutation behind a stale snapshot.
 */

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SenderNG.Core.Tests.Localization;

/// <summary>
/// The backup &amp; restore feature's formatted keys must declare exactly the
/// placeholders their call sites pass, in en/ru/de/fr/es, and the non-en
/// catalogs must actually translate them.
/// </summary>
public class BackupRestoreLocaleKeysTests
{
    /// <summary>
    /// The keys this guard pins, with the placeholder indices their call sites
    /// pass. Keep in step with those call sites:
    /// <list type="bullet">
    /// <item><c>module.grbl-settings.restoreVerifyMismatch</c> —
    /// <c>GrblSettingsViewModel.ApplyRestoreAsync</c> formats it with
    /// (missed.Count, string.Join(", ", missed)) ⇒ needs {0} and {1}.</item>
    /// <item><c>appSettings.backup.savedWarning</c> —
    /// <c>BackupSettingsLeaf.BackupAsync</c> formats it with (file count,
    /// destination path, validation errors) ⇒ needs {0}, {1} and {2}.</item>
    /// </list>
    /// </summary>
    private static readonly (string Key, int[] Placeholders)[] FeatureKeys =
    [
        ("module.grbl-settings.restoreVerifyMismatch", [0, 1]),
        ("appSettings.backup.savedWarning", [0, 1, 2]),
    ];

    private static readonly Regex Placeholder = new(@"\{(\d+)", RegexOptions.Compiled);

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Every_catalog_declares_the_placeholders_its_call_site_passes(string code)
    {
        var path = CatalogPath(code);
        var strings = Strings(path);
        var en = code == "en" ? strings : Strings(CatalogPath("en"));

        foreach (var (key, expected) in FeatureKeys)
        {
            Assert.True(strings.TryGetValue(key, out var value),
                $"{path}: no \"{key}\" string");
            Assert.False(string.IsNullOrWhiteSpace(value),
                $"{path}: \"{key}\" is blank");

            var actual = Placeholders(value!);
            Assert.True(expected.SequenceEqual(actual),
                $"{path}: \"{key}\" must declare exactly {Show(expected)} — its call site passes "
                + $"{expected.Length} argument(s) and string.Format silently drops any surplus; "
                + $"found {Show(actual)} in \"{value}\"");

            if (code != "en")
            {
                Assert.True(en.TryGetValue(key, out var enValue),
                    $"Locale/en.json has no \"{key}\" string");
                Assert.False(string.Equals(value, enValue, StringComparison.Ordinal),
                    $"{path}: \"{key}\" is still the English text — translate it");
            }
        }
    }

    /// <summary>The English text is specified verbatim by the review work order
    /// (F15) and is also the literal the call site falls back to when no locale is
    /// loaded, so the two must not drift apart.</summary>
    [Fact]
    public void The_english_catalog_carries_the_specified_savedWarning_text()
    {
        var en = Strings(CatalogPath("en"));
        Assert.True(en.TryGetValue("appSettings.backup.savedWarning", out var value),
            "Locale/en.json has no \"appSettings.backup.savedWarning\" string");
        Assert.Equal(
            "Backed up {0} files to {1}. Warning: this archive may not restore ({2}).",
            value);
    }

    private static string CatalogPath(string code) =>
        Path.Combine(RepoRoot(), "src", "SenderNG", "Locale", code + ".json");

    private static Dictionary<string, string> Strings(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("strings", out var strings))
            foreach (var p in strings.EnumerateObject())
                result[p.Name] = p.Value.GetString() ?? string.Empty;
        return result;
    }

    private static int[] Placeholders(string value) =>
        Placeholder.Matches(value)
            .Cast<Match>()
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(i => i)
            .ToArray();

    private static string Show(int[] placeholders) =>
        placeholders.Length == 0
            ? "(no placeholders)"
            : string.Join(" ", placeholders.Select(i => "{" + i.ToString(CultureInfo.InvariantCulture) + "}"));

    /// <summary>Walk up to the checkout root (a .git dir in a normal clone, a
    /// .git file in a worktree). Never matches a directory without both src/
    /// and the repo marker, so a mislaid test project cannot validate another
    /// checkout's catalogs.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if ((Directory.Exists(git) || File.Exists(git)) && Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
        }
        throw new FileNotFoundException("Could not locate the checkout root (no .git + src above the test binary).");
    }
}
