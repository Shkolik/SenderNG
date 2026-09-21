
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

using SenderNG.Core.Modules;
using SenderNG.ViewModels;
using Xunit;

namespace SenderNG.Tests;

public class AboutDialogModelTests
{
    [Fact]
    public void PluginDependency_OneLine_formats_name_version_license()
    {
        var dep = new PluginDependency("Avalonia", "12.0.0", "MIT");
        Assert.Equal("Avalonia v.12.0.0 · MIT", dep.OneLine);
    }

    [Fact]
    public void PluginDependency_OneLine_omits_blank_license()
    {
        var dep = new PluginDependency("Foo", "1.0", "");
        Assert.Equal("Foo v.1.0", dep.OneLine);
    }

    [Fact]
    public void ThirdPartyLibrary_OneLine_formats_name_version_license()
    {
        var lib = new ThirdPartyLibrary("Avalonia", "12.1.1", "MIT");
        Assert.Equal("Avalonia v.12.1.1 · MIT", lib.OneLine);
    }

    [Fact]
    public void ThirdPartyLibrary_OneLine_omits_null_license()
    {
        var lib = new ThirdPartyLibrary("Foo", "1.0");
        Assert.Equal("Foo v.1.0", lib.OneLine);
    }

    [Fact]
    public void ModuleInfoItem_AuthorLicense_combines_author_and_license()
    {
        var item = new ModuleInfoItem("HPGL", "1.0.0", "Andrew Shkolik", "BSD-3-Clause",
            [new PluginDependency("Avalonia", "12.0.0", "MIT")]);
        Assert.Equal("Andrew Shkolik · BSD-3-Clause", item.AuthorLicense);
    }
}
