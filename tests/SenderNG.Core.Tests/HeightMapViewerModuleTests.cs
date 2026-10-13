/*
 * HeightMapViewerModuleTests.cs
 *
 * The viewer ships as a true external plugin (own DLL, Modules/ scan). This test
 * pins the ScanDirectory discovery of the staged DLL + its staged locale,
 * mirroring the plotter-GL module test. The plugin's own contract is covered by
 * SenderNG.Modules.HeightMapViewer.Tests.
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

using System.Text.Json;
using CNC.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Core.Tests;

public class HeightMapViewerModuleTests
{
    [Fact]
    public void ScanDirectory_discovers_the_staged_heightmap_viewer_dll()
    {
        // The plugin DLL is staged into this project's OutputDir/Modules (same
        // Link trick the app csproj uses) — the real drop-in path.
        var dir = Path.Combine(AppContext.BaseDirectory, "Modules");
        Assert.True(Directory.Exists(dir), "plugin staging missing — rebuild the test project: " + dir);

        var sc = new ServiceCollection();
        ModuleCatalog.ScanDirectory(sc, dir);

        // LoadFrom context: match by full name, not compile-time type identity
        // (this project deliberately does not reference the plugin).
        Assert.Contains(sc, d => d.ServiceType.FullName == "SenderNG.Modules.HeightMapViewer.HeightMapViewerModule");

        using var sp = sc.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();
        Assert.Contains(modules, m => m.Id == "heightmap-viewer");
    }

    [Fact]
    public void Staged_locale_ships_the_viewer_keys()
    {
        // SenderNG.csproj stages the plugin's Locale/** to Modules/<id>/Locale.
        var path = Path.Combine(AppContext.BaseDirectory, "Modules", "heightmap-viewer", "Locale", "en.json");
        Assert.True(File.Exists(path), "locale staging missing — SenderNG.csproj should stage it to " + path);

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var strings = doc.RootElement.GetProperty("strings");
        foreach (var key in new[]
        {
            "module.heightmap-viewer.name",
            "module.heightmap-viewer.description",
            "module.heightmap-viewer.empty",
            "module.heightmap-viewer.title",
            "module.heightmap-viewer.reset",
            "module.heightmap-viewer.renderUnavailable",
            "module.heightmap-viewer.renderUnavailableHint",
        })
        {
            Assert.True(strings.TryGetProperty(key, out var v) && !string.IsNullOrWhiteSpace(v.GetString()),
                $"staged en.json is missing {key}");
        }
    }
}
