using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class CuttingViewModelTests
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
    public void Overrides_start_at_100_percent()
    {
        var vm = new CuttingViewModel(new RecordingGrbl([]));

        Assert.Equal(100, vm.FeedOverride);
        Assert.Equal(100, vm.SpindleOverride);
    }

    [Fact]
    public void Feed_up_down_steps_one_percent_and_sends()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        vm.FeedUpCommand.Execute(null);

        Assert.Equal(101, vm.FeedOverride);
        Assert.Equal("$FV=101", sent[0]);

        vm.FeedDownCommand.Execute(null);
        vm.FeedDownCommand.Execute(null);

        Assert.Equal(99, vm.FeedOverride);
        Assert.Equal(["$FV=101", "$FV=100", "$FV=99"], sent);
    }

    [Fact]
    public void Feed_coarse_steps_ten_percent()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        vm.FeedUp10Command.Execute(null);

        Assert.Equal(110, vm.FeedOverride);
        Assert.Equal("$FV=110", Assert.Single(sent));
    }

    [Fact]
    public void Spindle_override_adjusts_and_sends()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        vm.SpindleDown10Command.Execute(null);

        Assert.Equal(90, vm.SpindleOverride);
        Assert.Equal("$SV=90", Assert.Single(sent));
    }

    [Fact]
    public void Overrides_clamped_to_10_200()
    {
        var sent = new List<string>();
        var vm = new CuttingViewModel(new RecordingGrbl(sent));

        for (var i = 0; i < 300; i++)
            vm.FeedUp10Command.Execute(null);
        for (var i = 0; i < 300; i++)
            vm.SpindleDownCommand.Execute(null);

        Assert.Equal(200, vm.FeedOverride);
        Assert.Equal(10, vm.SpindleOverride);
        Assert.All(sent, l => Assert.Matches(@"^\$(FV|SV)=\d+$", l));
    }
}
