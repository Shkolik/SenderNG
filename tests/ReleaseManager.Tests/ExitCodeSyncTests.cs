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

using System.Text.RegularExpressions;
using Xunit;

namespace ReleaseManager.Tests;

public class ExitCodeSyncTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "ReleaseManager")))
                return dir.FullName;
            dir = dir.Parent;
        }

        // Fails loudly rather than silently skipping: this suite is meant to run
        // against a checkout (it does, under `dotnet test` from the repo). If it
        // ever runs from a package without one, we want to know.
        throw new DirectoryNotFoundException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Readme_exit_code_table_matches_the_ExitCodes_constants()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "ReleaseManager", "README.md"))
                          .Replace("\r\n", "\n");

        var heading = readme.IndexOf("## Exit codes", StringComparison.Ordinal);
        Assert.True(heading >= 0, "README.md no longer has an `## Exit codes` section");

        // Bounded to the first table under the heading (heading -> first blank line
        // after that table starts) so a later, unrelated table cannot silently
        // satisfy this assertion.
        var tableStart = readme.IndexOf("\n|", heading, StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "`## Exit codes` is no longer followed by a table");
        var tableEnd = readme.IndexOf("\n\n", tableStart, StringComparison.Ordinal);
        Assert.True(tableEnd >= 0, "the exit-code table is no longer followed by a blank line");
        var table = readme[heading..tableEnd];

        var rows = Regex.Matches(table, @"^\|\s*(\d+)\s*\|", RegexOptions.Multiline)
                        .Select(m => int.Parse(m.Groups[1].Value))
                        .ToArray();

        Assert.Equal(
            new[] { ExitCodes.Success, ExitCodes.Cli, ExitCodes.Build, ExitCodes.Staging, ExitCodes.UnsupportedPlatform, ExitCodes.Cancelled },
            rows);
    }

    [Fact]
    public void Linux_build_script_switch_matches_the_ExitCodes_constants()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "build-linux-release.ps1"));

        // Bounded to the `switch ($code)` block (from the switch to the `exit $code`
        // that leaves it) so numeric labels elsewhere in the script cannot match.
        // The catch-block `exit 130` above the switch is a distinct early-return for
        // Ctrl-C during the wsl invocation, not a switch arm, so it is deliberately
        // outside this list; the switch's own `130` arm covers a cancelled child.
        var switchStart = script.IndexOf("switch ($code)", StringComparison.Ordinal);
        Assert.True(switchStart >= 0, "build-linux-release.ps1 no longer has a `switch ($code)` block");
        var switchEnd = script.IndexOf("exit $code", switchStart, StringComparison.Ordinal);
        Assert.True(switchEnd >= 0, "`switch ($code)` is no longer followed by `exit $code`");
        var block = script[switchStart..switchEnd];

        var arms = Regex.Matches(block, @"^\s*(\d+)\s*\{", RegexOptions.Multiline)
                        .Select(m => int.Parse(m.Groups[1].Value))
                        .ToArray();

        Assert.Equal(
            new[] { ExitCodes.Cli, ExitCodes.Build, ExitCodes.Staging, ExitCodes.UnsupportedPlatform, ExitCodes.Cancelled },
            arms);
    }
}
