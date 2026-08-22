using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class CoolantViewModelTests
{
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
    public void Coolant_starts_off()
    {
        var vm = new CoolantViewModel(new RecordingGrbl([]));

        Assert.False(vm.IsFlood);
        Assert.False(vm.IsMist);
    }

    [Fact]
    public async Task Flood_toggle_sends_M8_then_M9()
    {
        var sent = new List<string>();
        var vm = new CoolantViewModel(new RecordingGrbl(sent));

        await vm.ToggleFloodAsync();
        Assert.True(vm.IsFlood);

        await vm.ToggleFloodAsync();

        Assert.False(vm.IsFlood);
        Assert.Equal(["M8", "M9"], sent);
    }

    [Fact]
    public async Task Mist_toggle_sends_M7_then_M9()
    {
        var sent = new List<string>();
        var vm = new CoolantViewModel(new RecordingGrbl(sent));

        await vm.ToggleMistAsync();
        Assert.True(vm.IsMist);

        await vm.ToggleMistAsync();

        Assert.False(vm.IsMist);
        Assert.Equal(["M7", "M9"], sent);
    }

    [Fact]
    public void Commands_are_wired()
    {
        var sent = new List<string>();
        var vm = new CoolantViewModel(new RecordingGrbl(sent));

        vm.FloodCommand.Execute(null);
        vm.MistCommand.Execute(null);

        Assert.True(vm.IsFlood);
        Assert.True(vm.IsMist);
    }
}
