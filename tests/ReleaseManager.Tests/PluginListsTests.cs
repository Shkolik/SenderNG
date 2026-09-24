/*
 * PluginListsTests.cs - plugin manifest, 1st-party set and selection resolution tests
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

public class PluginListsTests
{
    private static readonly List<string> NoAdHoc = [];

    [Fact]
    public void FirstParty_is_the_five_in_solution_plugins()
    {
        var ids = FirstPartyPlugins.All.Select(p => p.Id).OrderBy(x => x).ToArray();
        Assert.Equal(["excellon", "hpgl", "plotter", "plotter-nex", "probing"], ids);
    }

    [Fact]
    public void Parse_reads_manifest_entries()
    {
        var json = """{ "plugins": [ { "id": "gerber", "project": "ext/Gerber/Gerber.csproj" } ] }""";
        var entries = PluginsManifest.Parse(json);
        var entry = Assert.Single(entries);
        Assert.Equal("gerber", entry.Id);
        Assert.Equal("ext/Gerber/Gerber.csproj", entry.Project);
        Assert.Null(entry.Version);
    }

    [Fact]
    public void Parse_accepts_version_override()
    {
        var json = """{ "plugins": [ { "id": "gerber", "project": "ext/Gerber/Gerber.csproj", "version": "1.2.0" } ] }""";
        Assert.Equal("1.2.0", PluginsManifest.Parse(json)[0].Version);
    }

    [Fact]
    public void Parse_throws_on_malformed_json()
    {
        Assert.Throws<CliException>(() => PluginsManifest.Parse("{ not json"));
    }

    [Fact]
    public void Parse_missing_plugins_key_returns_empty()
    {
        Assert.Empty(PluginsManifest.Parse("{}"));
    }

    [Fact]
    public void Parse_missing_plugins_key_nested_props_still_empty()
    {
        Assert.Empty(PluginsManifest.Parse("""{"other":1,"arr":[1,2]}"""));
    }

    [Fact]
    public void Parse_non_object_plugin_entry_throws_CliException()
    {
        var ex = Assert.Throws<CliException>(() => PluginsManifest.Parse("""{"plugins":[42]}"""));
        Assert.Contains("Malformed plugins.json", ex.Message);
    }

    [Fact]
    public void FromAdHoc_derives_id_from_project_name()
    {
        var entry = PluginsManifest.FromAdHoc("ext/MyPlugin/MyPlugin.csproj");
        Assert.Equal("MyPlugin", entry.Id);
        Assert.Equal("ext/MyPlugin/MyPlugin.csproj", entry.Project);
    }

    [Fact]
    public void Resolve_none_returns_empty()
    {
        var options = new ReleaseOptions { Selection = PluginSelection.None, AdHocPluginPaths = NoAdHoc };
        var list = PluginLists.Resolve(TempRepo.New(), options, new SilentLog());
        Assert.Empty(list);
    }

    [Fact]
    public void Resolve_all_collides_with_first_party()
    {
        var repo = TempRepo.New();
        var ex = Assert.Throws<CliException>(() => PluginLists.Resolve(repo, optionsWithManifest(repo, "excellon", "ext/Excellon/Excellon.csproj"), new SilentLog()));
        Assert.Contains("collision", ex.Message);
    }

    [Fact]
    public void Resolve_subset_filters_and_rejects_unknown_id()
    {
        var repo = TempRepo.New();
        var options = new ReleaseOptions
        {
            Selection = PluginSelection.Subset,
            PluginIds = ["hpgl"],
            AdHocPluginPaths = NoAdHoc
        };
        var list = PluginLists.Resolve(repo, options, new SilentLog());
        Assert.Equal(["hpgl"], list.Select(p => p.Id));

        options = options with { PluginIds = ["nope"] };
        Assert.Throws<CliException>(() => PluginLists.Resolve(repo, options, new SilentLog()));
    }

    private static ReleaseOptions optionsWithManifest(string repo, string id, string project)
    {
        Directory.CreateDirectory(Path.Combine(repo, "tools", "ReleaseManager"));
        File.WriteAllText(
            Path.Combine(repo, "tools", "ReleaseManager", "plugins.json"),
            $$"""{ "plugins": [ { "id": "{{id}}", "project": "{{project}}" } ] }""");
        return new ReleaseOptions { Selection = PluginSelection.All, AdHocPluginPaths = NoAdHoc };
    }
}
