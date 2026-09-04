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

using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class SettingsServiceTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_creates_default_when_missing()
    {
        var s = new SettingsService(TempDir());
        var a = s.Load();
        Assert.NotNull(a);
        Assert.Equal(MachineProfile.DefaultId, a.Id);
        Assert.Equal(3, a.DemoAxes);
    }

    [Fact]
    public void Save_persists_profile_file()
    {
        var tmp = TempDir();
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);
        var a = svc.Load();
        a.DemoAxes = 5;
        svc.Save(a);
        // The legacy flat settings.json is superseded by profiles/<id>.json.
        Assert.False(File.Exists(Path.Combine(tmp, "settings.json")));
        Assert.True(File.Exists(Path.Combine(tmp, "profiles", a.Id + ".json")));
    }

    [Fact]
    public void Load_roundtrips_saved_settings()
    {
        var tmp = TempDir();
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);
        var saved = svc.Load();
        saved.MachineTypeId = MachineTypes.Foam;
        saved.DemoAxes = 4;
        saved.Connection.SerialPort = "COM9";
        saved.Connection.SerialBaudRate = 250000;
        svc.Save(saved);

        var loaded = new SettingsService(tmp).Load();
        Assert.Equal(MachineTypes.Foam, loaded.MachineTypeId);
        Assert.Equal(4, loaded.DemoAxes);
        Assert.Equal("COM9", loaded.Connection.SerialPort);
        Assert.Equal(250000, loaded.Connection.SerialBaudRate);
    }

    [Fact]
    public void Load_returns_cached_instance_so_all_consumers_share_one_graph()
    {
        var tmp = TempDir();
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);

        var first = svc.Load();
        var second = svc.Load();

        Assert.Same(first, second);
    }

    [Fact]
    public void Save_updates_the_cached_instance()
    {
        var tmp = TempDir();
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);

        var cached = svc.Load();
        var replacement = new MachineProfile { DemoAxes = 5 };
        svc.Save(replacement);

        Assert.Same(replacement, svc.Load());
        Assert.NotSame(cached, svc.Load());
    }
}
