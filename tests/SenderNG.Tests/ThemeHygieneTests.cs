/*
 * ThemeHygieneTests.cs - enforces the zero-literal rule for view AXAML
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

using Xunit;

namespace SenderNG.Tests;

public class ColorLiteralScannerTests
{
    [Fact]
    public void Finds_hex_and_named_but_not_transparent_or_comments()
    {
        const string xaml = """
            <!-- <Border Background="#f6f6f6" BorderBrush="White" /> -->
            <Border Background="#eee" BorderBrush="Transparent" />
            <TextBlock Foreground="Black" />
            """;
        var found = ColorLiteralScanner.Find(xaml);
        Assert.Equal(new[] { "#eee", "Black" }.OrderBy(x => x), found.OrderBy(x => x));
    }

    [Fact]
    public void Does_not_flag_hash_sequences_inside_entities_or_words()
    {
        Assert.Empty(ColorLiteralScanner.Find("""<TextBlock Text="&#x2713; #abcXYZ" />"""));
    }
}

public class ThemeHygieneTests
{
    [Fact]
    public void No_view_axaml_outside_the_theme_file_contains_color_literals()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        var views = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.axaml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel => rel != "src/SenderNG/Themes/SenderTheme.axaml");

        foreach (var rel in views)
        {
            var literals = ColorLiteralScanner.Find(File.ReadAllText(Path.Combine(root, rel)));
            if (literals.Count > 0)
                offenders.Add($"{rel} ({string.Join(", ", literals.Distinct())})");
        }

        Assert.True(offenders.Count == 0,
            "raw color literals in view AXAML (convert to Sender.* tokens):\n" + string.Join("\n", offenders));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) && File.Exists(Path.Combine(dir.FullName, "SenderNG.sln")))
                return dir.FullName;
        throw new FileNotFoundException("Could not locate the repo root above the test binary.");
    }
}
