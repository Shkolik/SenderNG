/*
 * CurationTests.cs - tests for the bundle-copy curation rules
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

public class CurationTests
{
    [Theory]
    [InlineData("SenderNG.exe")]
    [InlineData("SenderNG.dll")]
    [InlineData("SenderNG.deps.json")]
    [InlineData("SenderNG.runtimeconfig.json")]
    [InlineData("SenderNG.Core.dll")]
    [InlineData("Modules/SenderNG.Modules.Common.dll")]
    [InlineData("Modules/SenderNG.Modules.Common.Render.dll")]
    [InlineData("Modules/Silk.NET.OpenGL.dll")]
    [InlineData("Locale/en.json")]
    [InlineData("runtimes/win-x64/native/something.dll")]
    public void IsExcluded_false_for_shipped_files(string path)
    {
        Assert.False(Curation.IsExcluded(path));
    }

    [Theory]
    [InlineData("SenderNG.pdb")]
    [InlineData("SenderNG.Core.pdb")]
    [InlineData("Avalonia.xml")]
    [InlineData("Avalonia.DesignerSupport.dll")]
    [InlineData("Avalonia.Remote.Protocol.dll")]
    [InlineData("AvaloniaUI.DiagnosticsSupport.Avalonia.dll")]
    [InlineData("global.json")]
    [InlineData("profiles/machine1.json")]
    [InlineData("profiles/global.json")]
    public void IsExcluded_true_for_dev_only_files(string path)
    {
        Assert.True(Curation.IsExcluded(path));
    }

    [Theory]
    [InlineData("SenderNG.Core.dll", true)]
    [InlineData("CNC.Core.dll", true)]
    [InlineData("SenderNG.Modules.Common.dll", true)]
    [InlineData("SenderNG.Modules.Common.Render.dll", true)]
    [InlineData("Silk.NET.Core.dll", true)]
    [InlineData("Silk.NET.Maths.dll", true)]
    [InlineData("Silk.NET.OpenGL.dll", true)]
    [InlineData("Avalonia.dll", true)]
    [InlineData("Avalonia.Win32.dll", true)]
    [InlineData("SenderNG.Modules.Excellon.dll", false)]
    [InlineData("SkiaSharp.dll", false)]
    public void IsHostOwned_matches_app_owned_assemblies(string fileName, bool expected)
    {
        Assert.Equal(expected, Curation.IsHostOwned(fileName));
    }

    [Fact]
    public void CopyBundle_preserves_structure_and_skips_excluded()
    {
        var publish = TempDir.New();
        var bundle = TempDir.New();
        Directory.CreateDirectory(Path.Combine(publish, "runtimes", "win-x64", "native"));
        Directory.CreateDirectory(Path.Combine(publish, "Locale"));
        Directory.CreateDirectory(Path.Combine(publish, "Modules", "excellon", "Locale"));
        File.WriteAllText(Path.Combine(publish, "SenderNG.exe"), "exe");
        File.WriteAllText(Path.Combine(publish, "SenderNG.pdb"), "pdb");
        File.WriteAllText(Path.Combine(publish, "global.json"), "{}");
        File.WriteAllText(Path.Combine(publish, "Locale", "en.json"), "{}");
        File.WriteAllText(Path.Combine(publish, "runtimes", "win-x64", "native", "lib.so"), "n");
        File.WriteAllText(Path.Combine(publish, "Modules", "excellon", "Locale", "en.json"), "{}");

        Curation.CopyBundle(publish, bundle, new SilentLog());

        Assert.True(File.Exists(Path.Combine(bundle, "SenderNG.exe")));
        Assert.False(File.Exists(Path.Combine(bundle, "SenderNG.pdb")));
        Assert.False(File.Exists(Path.Combine(bundle, "global.json")));
        Assert.True(File.Exists(Path.Combine(bundle, "Locale", "en.json")));
        Assert.True(File.Exists(Path.Combine(bundle, "runtimes", "win-x64", "native", "lib.so")));
        Assert.True(File.Exists(Path.Combine(bundle, "Modules", "excellon", "Locale", "en.json")));
    }

    [Fact]
    public void StripModulesToSdk_keeps_only_the_sdk_assembly()
    {
        var modules = TempDir.New();
        File.WriteAllText(Path.Combine(modules, "SenderNG.Modules.Common.dll"), "sdk");
        File.WriteAllText(Path.Combine(modules, "SenderNG.Modules.Common.Render.dll"), "render");
        File.WriteAllText(Path.Combine(modules, "Silk.NET.Core.dll"), "silk");
        File.WriteAllText(Path.Combine(modules, "Silk.NET.Maths.dll"), "silk");
        File.WriteAllText(Path.Combine(modules, "Silk.NET.OpenGL.dll"), "silk");
        File.WriteAllText(Path.Combine(modules, "SenderNG.Modules.Excellon.dll"), "mod");
        Directory.CreateDirectory(Path.Combine(modules, "excellon", "Locale"));
        File.WriteAllText(Path.Combine(modules, "excellon", "Locale", "en.json"), "{}");
        Directory.CreateDirectory(Path.Combine(modules, "hpgl"));
        File.WriteAllText(Path.Combine(modules, "hpgl", "ThirdPartyDep.dll"), "lib");

        Curation.StripModulesToSdk(modules);

        Assert.True(File.Exists(Path.Combine(modules, "SenderNG.Modules.Common.dll")));
        Assert.True(File.Exists(Path.Combine(modules, "SenderNG.Modules.Common.Render.dll")));
        Assert.True(File.Exists(Path.Combine(modules, "Silk.NET.Core.dll")));
        Assert.True(File.Exists(Path.Combine(modules, "Silk.NET.Maths.dll")));
        Assert.True(File.Exists(Path.Combine(modules, "Silk.NET.OpenGL.dll")));
        Assert.False(File.Exists(Path.Combine(modules, "SenderNG.Modules.Excellon.dll")));
        Assert.False(Directory.Exists(Path.Combine(modules, "excellon")));
        Assert.False(Directory.Exists(Path.Combine(modules, "hpgl")));
    }
}
