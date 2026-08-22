using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class ProbeViewModelTests : IDisposable
{
    private readonly SettingsService _settings;

    public ProbeViewModelTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(dir);
    }

    private sealed class RecordingGrbl(List<string> sent) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line)
        {
            sent.Add(line);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Z_probe_sends_G38_2_negative_Z_with_settings_depth_feed()
    {
        var sent = new List<string>();
        var vm = new ProbeViewModel(new RecordingGrbl(sent), _settings);

        vm.ProbeZCommand.Execute(null);

        Assert.Equal(["G38.2 Z-10 F100"], sent);
        Assert.Equal("G38.2 Z-10 F100", vm.LastProbeResult);
    }

    [Fact]
    public void XMinus_sends_negative_X_direction()
    {
        var sent = new List<string>();
        var vm = new ProbeViewModel(new RecordingGrbl(sent), _settings);

        vm.ProbeXMinusCommand.Execute(null);

        Assert.Equal(["G38.2 X-10 F100"], sent);
    }

    [Fact]
    public void YPlus_sends_positive_Y_direction()
    {
        var sent = new List<string>();
        var vm = new ProbeViewModel(new RecordingGrbl(sent), _settings);

        vm.ProbeYPlusCommand.Execute(null);

        Assert.Equal(["G38.2 Y10 F100"], sent);
    }

    [Fact]
    public void Settings_roundtrip_persists_depth_and_feed()
    {
        var first = new ProbeViewModel(new RecordingGrbl([]), _settings);
        first.ProbeDepth = 5.5;
        first.ProbeFeed = 250;
        first.Save();

        var sent = new List<string>();
        var second = new ProbeViewModel(new RecordingGrbl(sent), _settings);

        Assert.Equal(5.5, second.ProbeDepth);
        Assert.Equal(250, second.ProbeFeed);

        second.ProbeZCommand.Execute(null);
        Assert.Equal(["G38.2 Z-5.5 F250"], sent);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
