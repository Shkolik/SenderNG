/*
 * LocKeySourceTests.cs - drift guard: a LocText key literal in source must exist
 * in that project's own Locale/en.json.
 *
 * LocaleParityTests guards en -> every other language; nothing guards C# -> en,
 * so a key absent from ALL catalogs passes every existing assertion and silently
 * degrades to the English fallback at runtime (LocText.Resolve). This test closes
 * that half. The two compose: C# -> en here, en -> non-en there.
 *
 * Reads the SOURCE tree, not the staged copy, for the same reason
 * LocaleParityTests does: a mutation run edits a file and re-runs without a
 * rebuild, and a staged copy would hide it behind a stale snapshot.
 *
 * Discovery walks src/* for directories owning a Locale/en.json, so a future
 * module needs no edit here.
 */

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SenderNG.Core.Tests.Localization;

/// <summary>
/// Every <c>LocText.Resolve</c> / <c>LocText.Format</c> string literal in a
/// project must be a key of that project's <c>Locale/en.json</c>. Non-literal
/// first arguments are not matched: a ternary over two literals and wrappers
/// that forward a caller's key both hide literals from this scan. A literal
/// introduced inside those shapes must be checked against Locale/en.json by hand.
/// </summary>
public class LocKeySourceTests
{
    private static readonly Regex LocKey = new(
        @"LocText\.(?:Resolve|Format)\(\s*""(?<k>[a-zA-Z0-9_.\-]+)""",
        RegexOptions.Compiled);

    /// <summary>Every src/* directory that owns a Locale/en.json, absolute paths.</summary>
    public static TheoryData<string> ProjectDirs
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var dir in Directory.GetDirectories(Path.Combine(RepoRoot(), "src"))
                         .OrderBy(d => d, StringComparer.Ordinal))
                if (File.Exists(Path.Combine(dir, "Locale", "en.json")))
                    data.Add(dir);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ProjectDirs))]
    public void Every_LocText_key_literal_exists_in_the_projects_english_catalog(string dir)
    {
        var en = EnglishKeys(dir);
        var missing = Scan(dir)
            .Where(kv => !en.Contains(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key} ({string.Join(", ", kv.Value)})")
            .ToList();

        Assert.True(missing.Count == 0,
            $"{Path.GetFileName(dir)} is missing {missing.Count} LocText key(s): {string.Join("; ", missing)}");
    }

    [Fact]
    public void The_scan_finds_the_core_apps_keys()
    {
        var count = Scan(Path.Combine(RepoRoot(), "src", "SenderNG")).Count;
        Assert.True(count > 0,
            $"the scan found no LocText keys in src/SenderNG - RepoRoot() or the regex is broken (found {count})");
    }

    /// <summary>Key literal -> every "File.cs:line" it appears at, in one project.</summary>
    private static Dictionary<string, List<string>> Scan(string projectDir)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
                continue;

            var text = File.ReadAllText(file, Encoding.UTF8);
            foreach (Match m in LocKey.Matches(text))
            {
                var key = m.Groups["k"].Value;
                if (!found.TryGetValue(key, out var sites))
                    found[key] = sites = new List<string>();
                var line = text.Substring(0, m.Index).Count(c => c == '\n') + 1;
                sites.Add($"{Path.GetFileName(file)}:{line}");
            }
        }
        return found;
    }

    private static HashSet<string> EnglishKeys(string projectDir)
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(projectDir, "Locale", "en.json")));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("strings", out var strings))
            foreach (var p in strings.EnumerateObject())
                keys.Add(p.Name);
        return keys;
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
