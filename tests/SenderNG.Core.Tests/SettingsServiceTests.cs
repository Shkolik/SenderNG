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
        Assert.Equal(3, a.DemoAxes);
    }

    [Fact]
    public void Save_persists_portable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);
        var a = new AppSettings { DemoAxes = 5 };
        svc.Save(a);
        Assert.True(File.Exists(Path.Combine(tmp, "settings.json")));
    }

    [Fact]
    public void Load_roundtrips_saved_settings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmp);
        var svc = new SettingsService(tmp);
        svc.Save(new AppSettings { DemoAxes = 4, Mode = "Laser" });
        var loaded = svc.Load();
        Assert.Equal(4, loaded.DemoAxes);
        Assert.Equal("Laser", loaded.Mode);
    }
}
