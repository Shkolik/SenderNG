/*
 * LocaleParityTests.cs
 *
 * Discovery-driven drift guard over EVERY locale catalog in the source tree:
 * the core app's Locale/ plus the five modules'. Each non-en file must be a
 * strict display override of its en base — all en `strings` keys present,
 * identical grblSettings / grblSettingGroups id sets, placeholders preserved,
 * no blank values, no UTF-8 BOM — and the core files must also be staged into
 * the app output (the copy the app actually parses).
 *
 * The theory walks the catalog directories from disk rather than listing
 * files, so the fr/de/es catalogs this plan goes on to add need no edit here;
 * Tasks 7–12 run the class as their acceptance gate
 * (--filter FullyQualifiedName~LocaleParity).
 *
 * SOURCE tree, not the staged copy, is what the per-file checks read: a
 * mutation run edits a file and re-runs the test without a rebuild, and a
 * staged copy would hide the mutation behind a stale snapshot. The staged copy
 * gets its own check — The_staged_core_catalogs_ship_every_source_locale —
 * because that is the one the app reads, and a csproj glob that stopped
 * staging a locale would otherwise leave every label on its English fallback
 * with nothing else noticing.
 */

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SenderNG.Core.Tests.Localization;

/// <summary>
/// Discovery-driven drift guard over ALL locale catalogs (core + five modules).
/// Every non-en file must be a strict display override of its en base: all en
/// string keys present, identical grbl id sets, placeholders preserved, no
/// blanks, no BOM, and the core file staged into the app output. Adding a new
/// locale needs no edit here — the theory discovers the files.
/// </summary>
public class LocaleParityTests
{
    private static readonly string[] CatalogRel =
    [
        Path.Combine("src", "SenderNG", "Locale"),
        Path.Combine("src", "SenderNG.Modules.Excellon", "Locale"),
        Path.Combine("src", "SenderNG.Modules.Hpgl", "Locale"),
        Path.Combine("src", "SenderNG.Modules.Plotter", "Locale"),
        Path.Combine("src", "SenderNG.Modules.Plotter.Nex", "Locale"),
        Path.Combine("src", "SenderNG.Modules.Probing", "Locale"),
    ];

    /// <summary>Every non-en catalog file in the source tree, absolute paths.</summary>
    public static TheoryData<string> LocaleFiles
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var rel in CatalogRel)
            {
                var dir = Path.Combine(RepoRoot(), rel);
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                    if (!string.Equals(Path.GetFileName(file), "en.json", StringComparison.OrdinalIgnoreCase))
                        data.Add(file);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LocaleFiles))]
    public void Every_english_string_key_exists_in_the_locale(string file)
    {
        var en = Strings(enPath(file));
        var loc = Strings(file);
        var missing = en.Keys.Where(k => !loc.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            $"{file} is missing {missing.Count} en key(s), first: {string.Join(", ", missing.Take(10))}");
    }

    [Theory]
    [MemberData(nameof(LocaleFiles))]
    public void The_grbl_sections_carry_the_english_id_sets(string file)
    {
        using var enDoc = JsonDocument.Parse(File.ReadAllBytes(enPath(file)));
        using var locDoc = JsonDocument.Parse(File.ReadAllBytes(file));

        foreach (var section in new[] { "grblSettings", "grblSettingGroups" })
        {
            if (!enDoc.RootElement.TryGetProperty(section, out var enSection))
                continue; // module catalogs have strings only
            Assert.True(locDoc.RootElement.TryGetProperty(section, out var locSection),
                $"{file} has no \"{section}\" section but en has one");

            var enIds = Ids(enSection);
            var locIds = Ids(locSection);
            Assert.Equal(
                enIds.OrderBy(i => i),
                locIds.OrderBy(i => i));
        }
    }

    [Theory]
    [MemberData(nameof(LocaleFiles))]
    public void Placeholders_survive_translation(string file)
    {
        var en = Strings(enPath(file));
        var loc = Strings(file);
        foreach (var (key, enValue) in en)
        {
            if (!loc.TryGetValue(key, out var locValue))
                continue; // reported by Every_english_string_key_exists_in_the_locale
            foreach (Match m in Regex.Matches(enValue, @"\{[^{}]*\}"))
                Assert.True(locValue.Contains(m.Value, StringComparison.Ordinal),
                    $"{file}:{key} lost placeholder {m.Value} (locale value: {locValue})");
        }
    }

    [Theory]
    [MemberData(nameof(LocaleFiles))]
    public void No_value_is_blank(string file)
    {
        var blank = Strings(file).Where(kv => string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(blank.Count == 0, $"{file} has blank values: {string.Join(", ", blank)}");
    }

    [Theory]
    [MemberData(nameof(LocaleFiles))]
    public void The_file_has_no_utf8_bom(string file)
    {
        // Byte-level on purpose: File.ReadAllText(path, Encoding.UTF8) strips a
        // BOM, and JsonDocument.Parse(raw bytes) — how the app reads module
        // catalogs — throws on one. This is the only check that can see it.
        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{file} starts with a UTF-8 BOM");
    }

    [Fact]
    public void The_staged_core_catalogs_ship_every_source_locale()
    {
        var dir = Path.Combine(RepoRoot(), "src", "SenderNG", "Locale");
        foreach (var file in Directory.GetFiles(dir, "*.json"))
            Assert.True(
                File.Exists(Path.Combine(AppContext.BaseDirectory, "Locale", Path.GetFileName(file))),
                $"staged copy missing for {Path.GetFileName(file)} — SenderNG.csproj Locale glob broken?");
    }

    private static string enPath(string file) => Path.Combine(Path.GetDirectoryName(file)!, "en.json");

    private static Dictionary<string, string> Strings(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, string>();
        if (doc.RootElement.TryGetProperty("strings", out var strings))
            foreach (var p in strings.EnumerateObject())
                result[p.Name] = p.Value.GetString() ?? string.Empty;
        return result;
    }

    private static IEnumerable<int> Ids(JsonElement section)
    {
        foreach (var p in section.EnumerateObject())
            if (int.TryParse(p.Name, out var id))
                yield return id;
    }

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
