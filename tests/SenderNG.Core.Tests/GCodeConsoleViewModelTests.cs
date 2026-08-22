using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class GCodeConsoleViewModelTests
{
    private sealed class BehaviorSubjectGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject;
        public BehaviorSubjectGrbl(BehaviorSubject<MachineState> subject) => _subject = subject;
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    [Fact]
    public void Log_adds_line_to_console()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new GCodeConsoleViewModel(new BehaviorSubjectGrbl(subject));

        vm.Log("hello");

        Assert.Single(vm.ConsoleLines);
        Assert.Equal("hello", vm.ConsoleLines[0]);
    }

    [Fact]
    public async Task SendAsync_logs_line_and_forwards_to_grbl()
    {
        var sent = new List<string>();
        var grbl = new RecordingGrbl(sent);
        var vm = new GCodeConsoleViewModel(grbl);

        await vm.SendAsync("G0 X1");

        Assert.Equal(["G0 X1"], sent);
        Assert.Equal(["> G0 X1"], vm.ConsoleLines);
    }

    [Fact]
    public void LoadGCode_fills_gcode_lines()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new GCodeConsoleViewModel(new BehaviorSubjectGrbl(subject));

        vm.LoadGCode("G0 X0\nG0 Y0\r\nM30");

        Assert.Equal(3, vm.GCodeLines.Count);
        Assert.Equal("G0 X0", vm.GCodeLines[0]);
        Assert.Equal("M30", vm.GCodeLines[2]);
    }

    [Fact]
    public void LoadGCode_clears_previous_lines()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new GCodeConsoleViewModel(new BehaviorSubjectGrbl(subject));
        vm.LoadGCode("G0 X0\nG0 Y0");

        vm.LoadGCode("M2");

        Assert.Single(vm.GCodeLines);
        Assert.Equal("M2", vm.GCodeLines[0]);
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
}
