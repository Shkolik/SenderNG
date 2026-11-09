
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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plugins;

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

    private sealed class GatedGrbl(List<string> sent) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        private readonly TaskCompletionSource<string> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) { sent.Add(line); return Task.CompletedTask; }
        public Task<string> SendProgramLineAsync(string line, CancellationToken ct = default)
        {
            sent.Add(line);
            return _reply.Task;
        }
        public void Release() => _reply.TrySetResult("ok");
    }

    [Fact]
    public async Task Macro_run_is_refused_while_a_program_runs_and_lifts_when_it_ends()
    {
        // ioSender's MacroExecuteControl refuses while IsJobRunning: a macro line
        // queued between program blocks is a crash the operator cannot predict.
        var sent = new List<string>();
        var grbl = new GatedGrbl(sent);
        var program = new ProgramViewModel(grbl);
        program.LoadGCode("G0 X0\n");
        var run = program.RunAsync();
        await WaitUntil(() => program.IsRunning);

        var vm = new MacroViewModel(_settings, grbl, program);
        var macro = new MacroItem { Name = "Home", Code = "G28" };
        vm.Macros.Add(macro);
        vm.SelectedMacro = macro;

        Assert.False(vm.RunCommand.CanExecute(null));
        await vm.RunAsync(macro);                       // RunAsync is public: guard must hold
        Assert.Equal("Stop the running program first", vm.Status);
        Assert.DoesNotContain("G28", sent);

        grbl.Release();
        await run;
        await WaitUntil(() => !program.IsRunning);

        Assert.True(vm.RunCommand.CanExecute(null));
        await vm.RunAsync(macro);
        Assert.Contains("G28", sent);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !condition())
            await Task.Delay(20);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
