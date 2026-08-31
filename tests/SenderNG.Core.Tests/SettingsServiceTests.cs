
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

using System;
using System.IO;
using CNC.Core.Services;
using Xunit;

namespace SenderNG.Core.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void Load_creates_default_when_missing()
    {
        var s = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var a = s.Load();
        Assert.NotNull(a);
        Assert.Equal(3, a.Current.DemoAxes);
    }

    [Fact]
    public void Save_persists_portable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);
        var a = new AppSettings();
        a.Current.DemoAxes = 5;
        svc.Save(a);
        Assert.True(File.Exists(Path.Combine(tmp, "settings.json")));
    }

    [Fact]
    public void Load_roundtrips_saved_settings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);
        var saved = new AppSettings { Mode = "Foam" };
        saved.Current.DemoAxes = 4;
        saved.Current.Port = "COM9";
        saved.Current.BaudRate = 250000;
        svc.Save(saved);
        var loaded = svc.Load();
        Assert.Equal("Foam", loaded.Mode);
        Assert.Equal(4, loaded.Current.DemoAxes);
        Assert.Equal("COM9", loaded.Current.Port);
        Assert.Equal(250000, loaded.Current.BaudRate);
    }

    [Fact]
    public void Load_returns_cached_instance_so_all_consumers_share_one_graph()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);

        var first = svc.Load();
        var second = svc.Load();

        Assert.Same(first, second);
    }

    [Fact]
    public void Save_updates_the_cached_instance()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);

        var cached = svc.Load();
        var replacement = new AppSettings();
        replacement.Current.DemoAxes = 5;
        svc.Save(replacement);

        Assert.Same(replacement, svc.Load());
        Assert.NotSame(cached, svc.Load());
    }
}
