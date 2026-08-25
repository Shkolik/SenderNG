using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class MacroViewModelTests : IDisposable
{
    private readonly SettingsService _settings;

    public MacroViewModelTests()
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
        public void SendRealtime(byte code) => sent.Add($"RT:{code:X2}");
        
public Task SendAsync(string line)
        {
            sent.Add(line);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Run_macro_sends_lines_in_order()
    {
        var sent = new List<string>();
        var vm = new MacroViewModel(_settings, new RecordingGrbl(sent));
        var macro = new MacroItem { Name = "Home", Code = "G28\nG0 X0 Y0" };
        vm.Macros.Add(macro);

        await vm.RunAsync(macro);

        Assert.Equal(["G28", "G0 X0 Y0"], sent);
    }

    [Fact]
    public void RunCommand_executes_selected_macro()
    {
        var sent = new List<string>();
        var vm = new MacroViewModel(_settings, new RecordingGrbl(sent));
        vm.Macros.Add(new MacroItem { Name = "Probe", Code = "G38.2 Z-10" });
        vm.SelectedMacro = vm.Macros[0];

        vm.RunCommand.Execute(null);

        Assert.Equal(["G38.2 Z-10"], sent);
    }

    [Fact]
    public void Add_and_remove_persist_roundtrip()
    {
        var first = new MacroViewModel(_settings, new RecordingGrbl([]));
        var macro = new MacroItem { Name = "Tool change", Code = "M5\nM9\nG53 G0 Z0" };
        first.Macros.Add(macro);
        first.Save();

        var second = new MacroViewModel(_settings, new RecordingGrbl([]));

        var loaded = Assert.Single(second.Macros);
        Assert.Equal("Tool change", loaded.Name);
        Assert.Equal("M5\nM9\nG53 G0 Z0", loaded.Code);
    }

    [Fact]
    public void AddMacroCommand_adds_new_macro_and_selects_it()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));

        vm.AddMacroCommand.Execute(null);

        var added = Assert.Single(vm.Macros);
        Assert.Same(added, vm.SelectedMacro);
    }

    [Fact]
    public void RemoveMacroCommand_removes_selected_macro()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        var macro = new MacroItem { Name = "X", Code = "M30" };
        vm.Macros.Add(macro);
        vm.SelectedMacro = macro;

        vm.RemoveMacroCommand.Execute(null);

        Assert.Empty(vm.Macros);
        Assert.Null(vm.SelectedMacro);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
