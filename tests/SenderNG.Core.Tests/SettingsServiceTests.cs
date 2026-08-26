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
