/*
 * DotnetCliTests.cs - tests for the dotnet CLI facade
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

public class DotnetCliTests
{
    [Fact]
    public async Task ReadVersion_returns_project_version()
    {
        var csproj = WriteFixture(version: "1.2.3");
        var cli = new DotnetCli(new ProcessRunner());
        var version = await cli.ReadVersionAsync(Path.GetDirectoryName(csproj)!, csproj);
        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public async Task ReadVersion_returns_null_when_no_version_property()
    {
        var csproj = WriteFixture(version: null);
        var cli = new DotnetCli(new ProcessRunner());
        var version = await cli.ReadVersionAsync(Path.GetDirectoryName(csproj)!, csproj);
        Assert.Null(version);
    }

    private static string WriteFixture(string? version)
    {
        var dir = TempDir.New();
        var csproj = Path.Combine(dir, "Fixture.csproj");
        if (version is null)
        {
            File.WriteAllText(csproj, "<Project><PropertyGroup></PropertyGroup></Project>\n");
            return csproj;
        }
        File.WriteAllText(csproj,
            $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net9.0</TargetFramework>\n    <Version>{version}</Version>\n  </PropertyGroup>\n</Project>\n");
        return csproj;
    }
}
