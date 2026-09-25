/*
 * PluginStagingTests.cs - tests for the plugin drop-in staging rules
 */

/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

┬╖ Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

┬╖ Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

┬╖ Neither the name of the copyright holder nor the names of its contributors may
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

public class PluginStagingTests
{
    [Fact]
    public void OwnAssemblyFile_returns_unique_plugin_dll()
    {
        var publish = TempDir.New();
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Excellon.dll"), "mod");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Core.dll"), "core");
        File.WriteAllText(Path.Combine(publish, "Avalonia.Base.dll"), "avalonia");

        Assert.Equal("SenderNG.Modules.Excellon.dll", PluginStaging.OwnAssemblyFile(publish));
    }

    [Fact]
    public void OwnAssemblyFile_throws_when_none()
    {
        var publish = TempDir.New();
        File.WriteAllText(Path.Combine(publish, "SenderNG.Core.dll"), "core");
        var ex = Assert.Throws<StagingException>(() => PluginStaging.OwnAssemblyFile(publish));
        Assert.Contains("own assembly", ex.Message);
    }

    [Fact]
    public void OwnAssemblyFile_throws_when_multiple()
    {
        var publish = TempDir.New();
        File.WriteAllText(Path.Combine(publish, "A.dll"), "a");
        File.WriteAllText(Path.Combine(publish, "B.dll"), "b");
        Assert.Throws<StagingException>(() => PluginStaging.OwnAssemblyFile(publish));
    }

    [Fact]
    public void Stage_simple_plugin_flat_dll_and_locale_under_plugin_id()
    {
        var publish = TempDir.New();
        var project = TempDir.New();
        var dest = TempDir.New();
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Excellon.dll"), "mod");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Core.dll"), "core");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Common.dll"), "sdk");
        File.WriteAllText(Path.Combine(publish, "Avalonia.dll"), "avalonia");
        Directory.CreateDirectory(Path.Combine(project, "Locale", "excellon"));
        File.WriteAllText(Path.Combine(project, "Locale", "excellon", "en.json"), "{}");
        File.WriteAllText(Path.Combine(project, "Locale", "excellon", "ru.json"), "{}");

        PluginStaging.Stage(publish, project, "excellon", dest, new SilentLog());

        Assert.True(File.Exists(Path.Combine(dest, "SenderNG.Modules.Excellon.dll")));
        Assert.False(File.Exists(Path.Combine(dest, "SenderNG.Core.dll")));
        Assert.False(File.Exists(Path.Combine(dest, "SenderNG.Modules.Common.dll")));
        Assert.False(File.Exists(Path.Combine(dest, "excellon", "SenderNG.Core.dll")));
        var locale = Path.Combine(dest, "excellon", "Locale");
        Assert.True(File.Exists(Path.Combine(locale, "en.json")));
        Assert.True(File.Exists(Path.Combine(locale, "ru.json")));
    }

    [Fact]
    public void Stage_closure_plugin_flattens_dependencies_and_natives()
    {
        var publish = TempDir.New();
        var project = TempDir.New();
        var dest = TempDir.New();
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Plotter.Nex.dll"), "mod");
        File.WriteAllText(Path.Combine(publish, "HelixToolkit.Nex.Engine.dll"), "dep");
        File.WriteAllText(Path.Combine(publish, "Vortice.Vulkan.dll"), "dep2");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Plotter.Nex.deps.json"), "unwanted");
        Directory.CreateDirectory(Path.Combine(publish, "runtimes", "win-x64", "native"));
        File.WriteAllText(Path.Combine(publish, "runtimes", "win-x64", "native", "vma.dll"), "nat");

        PluginStaging.Stage(publish, project, "plotter-nex", dest, new SilentLog());

        Assert.True(File.Exists(Path.Combine(dest, "SenderNG.Modules.Plotter.Nex.dll")));
        var folder = Path.Combine(dest, "plotter-nex");
        Assert.True(File.Exists(Path.Combine(folder, "HelixToolkit.Nex.Engine.dll")));
        Assert.True(File.Exists(Path.Combine(folder, "Vortice.Vulkan.dll")));
        Assert.True(File.Exists(Path.Combine(folder, "vma.dll")));
        Assert.False(File.Exists(Path.Combine(folder, "SenderNG.Modules.Plotter.Nex.deps.json")));
    }

    [Fact]
    public void Stage_throws_when_publish_dir_does_not_exist()
    {
        var publish = Path.Combine(TempDir.New(), "missing");
        var project = TempDir.New();
        var dest = TempDir.New();

        var ex = Assert.Throws<StagingException>(() => PluginStaging.Stage(publish, project, "test", dest, new SilentLog()));
        Assert.Contains("does not exist", ex.Message);
    }
    [Fact]
    public void Stage_third_party_plugin_with_flat_project_locale_stages_locale_under_plugin_id()
    {
        // REAL 3rd-party layout (SenderNG.Modules.Probing): publishDir carries own DLL + dep DLLs but
        // NO Locale (plugin csproj does NOT wire Locale into publish output), and the project keeps
        // Locale FLAT at projectDir/Locale (en.json + ru.json), NOT Locale/<pluginId>.
        var publish = TempDir.New();
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Probing.dll"), "own");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Core.dll"), "core");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Common.dll"), "sdk");
        File.WriteAllText(Path.Combine(publish, "Avalonia.dll"), "avalonia");

        var project = TempDir.New();
        Directory.CreateDirectory(Path.Combine(project, "Locale"));
        File.WriteAllText(Path.Combine(project, "Locale", "en.json"), "{}");
        File.WriteAllText(Path.Combine(project, "Locale", "ru.json"), "{}");

        var dest = TempDir.New();
        PluginStaging.Stage(publish, project, "probing", dest, new SilentLog());

        Assert.True(File.Exists(Path.Combine(dest, "SenderNG.Modules.Probing.dll")));
        Assert.False(File.Exists(Path.Combine(dest, "SenderNG.Core.dll")));
        Assert.False(File.Exists(Path.Combine(dest, "SenderNG.Modules.Common.dll")));
        Assert.False(File.Exists(Path.Combine(dest, "probing", "SenderNG.Core.dll")));
        var locale = Path.Combine(dest, "probing", "Locale");
        Assert.True(File.Exists(Path.Combine(locale, "en.json")));
        Assert.True(File.Exists(Path.Combine(locale, "ru.json")));
    }

    [Fact]
    public void Stage_plugin_with_rid_split_managed_dependencies_stages_them_flat()
    {
        // REAL Avalonia/3rd-party publish splits managed deps under runtimes/<rid>/lib/<tfm>/,
        // which must be flattened into the staged plugin dir alongside natives.
        var publish = TempDir.New();
        File.WriteAllText(Path.Combine(publish, "SenderNG.Modules.Probing.dll"), "own");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Core.dll"), "core");
        var winLib = Path.Combine(publish, "runtimes", "win-x64", "lib", "net8.0");
        Directory.CreateDirectory(winLib);
        File.WriteAllText(Path.Combine(winLib, "System.IO.Ports.dll"), "riddep");

        var project = TempDir.New();
        var dest = TempDir.New();
        PluginStaging.Stage(publish, project, "probing", dest, new SilentLog());

        Assert.True(File.Exists(Path.Combine(dest, "probing", "System.IO.Ports.dll")));
    }
}

