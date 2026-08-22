using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class ConsoleViewModelTests
{
    [Fact]
    public void Log_adds_line()
    {
        var vm = new ConsoleViewModel(new BehaviorSubjectGrbl());

        vm.Log("hello");

        // BehaviorSubject replays the initial state line on subscribe; "hello" is appended last
        Assert.Equal("hello", vm.ConsoleLines[^1]);
    }

    [Fact]
    public void State_updates_append_received_lines()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3) { Status = "Idle" });
        var vm = new ConsoleViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState(3) { Status = "Run" });

        Assert.Contains(vm.ConsoleLines, l => l.Contains("< Idle"));
        Assert.Contains(vm.ConsoleLines, l => l.Contains("< Run"));
    }

    private sealed class BehaviorSubjectGrbl(BehaviorSubject<MachineState>? subject = null) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = subject ?? new BehaviorSubject<MachineState>(new MachineState(3));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }
}
