/*
 * CliTests.cs - CLI argument parsing tests
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

namespace ReleaseManager.Tests;

public class CliTests
{
    [Fact]
    public void Parse_empty_args_defaults_to_core_only()
    {
        var result = Cli.Parse([]);
        var options = Assert.IsType<ReleaseOptions>(result.Options);
        Assert.Equal(PluginSelection.None, options.Selection);
        Assert.False(options.NoZip);
        Assert.Equal("output", options.OutputDir);
    }

    [Fact]
    public void Parse_include_plugins_sets_all()
    {
        var options = Cli.Parse(["--include-plugins"]).Options!;
        Assert.Equal(PluginSelection.All, options.Selection);
    }

    [Fact]
    public void Parse_plugins_subset_implies_subset_selection()
    {
        var options = Cli.Parse(["--plugins", "hpgl,plotter"]).Options!;
        Assert.Equal(PluginSelection.Subset, options.Selection);
        Assert.Equal(["hpgl", "plotter"], options.PluginIds);
    }

    [Fact]
    public void Parse_plugins_overrides_include_plugins()
    {
        var options = Cli.Parse(["--include-plugins", "--plugins", "hpgl"]).Options!;
        Assert.Equal(PluginSelection.Subset, options.Selection);
        Assert.Equal(["hpgl"], options.PluginIds);
    }

    [Fact]
    public void Parse_repeatable_plugin_paths_collected()
    {
        var options = Cli.Parse(["--plugin", "a/A.csproj", "--plugin", "b/B.csproj"]).Options!;
        Assert.Equal(["a/A.csproj", "b/B.csproj"], options.AdHocPluginPaths);
    }

    [Fact]
    public void Parse_optional_flags()
    {
        var options = Cli.Parse(["--version", "1.2.3", "--out", "rel", "--skip-tests", "--no-zip", "--verbose"]).Options!;
        Assert.Equal("1.2.3", options.VersionOverride);
        Assert.Equal("rel", options.OutputDir);
        Assert.True(options.SkipTests);
        Assert.True(options.NoZip);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Parse_help_returns_usage()
    {
        var result = Cli.Parse(["--help"]);
        Assert.Null(result.Options);
        Assert.Contains("--include-plugins", result.Usage);
    }

    [Theory]
    [InlineData(new object[] { new[] { "--unknown" } })]
    [InlineData(new object[] { new[] { "--version" } })]
    [InlineData(new object[] { new[] { "--plugins" } })]
    [InlineData(new object[] { new[] { "--out" } })]
    public void Parse_rejects_unknown_or_missing_value_flags(string[] args)
    {
        Assert.Throws<CliException>(() => Cli.Parse(args));
    }

    [Fact]
    public void InteractiveText_collects_plugin_choices()
    {
        var input = new StringReader(
            "0.2.0\n" +
            "y\n" +
            "hpgl\n" +
            "\n" +
            "n\n");
        var output = new StringWriter();

        var options = Cli.InteractiveText(input, output, "0.1.0", ReleasePlatform.Win64, ["hpgl", "plotter"]);

        Assert.Equal("0.2.0", options.VersionOverride);
        Assert.Equal(PluginSelection.Subset, options.Selection);
        Assert.Equal(["hpgl"], options.PluginIds);
        Assert.True(options.NoZip);
    }
}
