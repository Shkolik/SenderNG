
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

using System.Collections.Specialized;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Services;
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

    [Fact]
    public void First_twelve_macros_get_F_key_shortcuts_and_the_rest_do_not()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        for (var i = 0; i < 13; i++)
            vm.Macros.Add(new MacroItem { Name = $"Macro {i + 1}", Code = "G0 X0" });

        Assert.Equal(12, vm.ToolbarMacros.Count);
        Assert.Equal("F1", vm.Macros[0].ShortcutKey);
        Assert.Equal("M1", vm.Macros[0].ShortcutLabel);
        Assert.Equal("F12", vm.Macros[11].ShortcutKey);
        Assert.True(vm.Macros[11].HasShortcut);
        Assert.Equal(string.Empty, vm.Macros[12].ShortcutKey);
        Assert.False(vm.Macros[12].HasShortcut);
        Assert.True(vm.HasToolbarMacros);
    }

    [Fact]
    public void An_empty_macro_gets_no_toolbar_button_and_keeps_its_slot_when_filled()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        vm.Macros.Add(new MacroItem { Name = "Empty", Code = "   " });
        vm.Macros.Add(new MacroItem { Name = "Home", Code = "G28" });

        // The empty macro owns F1 but is not a runnable action: no button.
        Assert.Equal(["Home"], vm.ToolbarMacros.Select(m => m.Name));
        Assert.Equal("F1", vm.Macros[0].ShortcutKey);

        vm.Macros[0].Code = "G0 X0";

        // It reappears at its own position, so the later macro keeps M2.
        Assert.Equal(["Empty", "Home"], vm.ToolbarMacros.Select(m => m.Name));
        Assert.Equal("M1", vm.ToolbarMacros[0].ToolbarLabel);
        Assert.Equal("M2", vm.ToolbarMacros[1].ToolbarLabel);
    }

    [Fact]
    public void Removing_a_macro_leaves_a_hole_and_the_next_macro_fills_it()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        for (var i = 0; i < 3; i++)
            vm.Macros.Add(new MacroItem { Name = $"Macro {i + 1}", Code = "G0 X0" });

        vm.Macros.RemoveAt(0);

        // The survivors keep their own keys; nothing moves onto a different F-key.
        Assert.Equal(["F2", "F3"], vm.Macros.Select(m => m.ShortcutKey));
        Assert.Equal(["M2", "M3"], vm.ToolbarMacros.Select(m => m.ToolbarLabel));

        // A new macro takes the freed slot instead of appending F4.
        vm.Macros.Add(new MacroItem { Name = "Filler", Code = "G28" });
        Assert.Equal("F1", vm.Macros[^1].ShortcutKey);
        Assert.Equal(["M1", "M2", "M3"], vm.ToolbarMacros.Select(m => m.ToolbarLabel));

        vm.Macros.Clear();

        Assert.Empty(vm.ToolbarMacros);
        Assert.False(vm.HasToolbarMacros);
    }

    [Fact]
    public void Deleting_a_macro_does_not_move_later_macros_onto_other_keys()
    {
        // The reported regression: ten macros, delete #4 — the button that runs
        // "Macro 5" must still be labeled M5 (tooltip and label agree).
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        for (var i = 0; i < 10; i++)
            vm.Macros.Add(new MacroItem { Name = $"Macro {i + 1}", Code = "G28" });

        vm.Macros.RemoveAt(3);

        Assert.Equal(["M1", "M2", "M3", "M5", "M6", "M7", "M8", "M9", "M10"],
            vm.ToolbarMacros.Select(m => m.ToolbarLabel));
        Assert.Equal("Macro 5", vm.ToolbarMacros[3].Name);
        Assert.Equal("F5", vm.ToolbarMacros[3].ShortcutKey);
    }

    [Fact]
    public void Slots_survive_a_reload_and_a_deleted_slot_stays_empty()
    {
        var first = new MacroViewModel(_settings, new RecordingGrbl([]));
        first.Macros.Add(new MacroItem { Name = "A", Code = "G28" });
        first.Macros.Add(new MacroItem { Name = "B", Code = "G28" });
        first.Macros.Add(new MacroItem { Name = "C", Code = "G28" });
        first.Macros.RemoveAt(0);
        first.Save();

        var second = new MacroViewModel(_settings, new RecordingGrbl([]));

        Assert.Equal(["F2", "F3"], second.Macros.Select(m => m.ShortcutKey));
        Assert.Equal(["M2", "M3"], second.ToolbarMacros.Select(m => m.ToolbarLabel));

        // A macro added after the reload fills the empty F1 rather than shifting.
        second.Macros.Add(new MacroItem { Name = "D", Code = "G28" });
        Assert.Equal("F1", second.Macros[^1].ShortcutKey);
    }

    [Fact]
    public void A_profile_from_before_slots_is_migrated_by_the_default_names()
    {
        // The reported state: the list is already missing macro 4 (names 1..10
        // minus 4). A positional renumber would put "Macro 5" on M4; the default
        // name says it belongs on M5.
        var settings = _settings.Load();
        settings.ModuleSettings["Macros"] = Enumerable.Range(1, 10)
            .Where(n => n != 4)
            .Select(n => new Dictionary<string, object> { ["Name"] = $"Macro {n}", ["Code"] = "G28" })
            .ToList();

        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));

        Assert.Equal(["M1", "M2", "M3", "M5", "M6", "M7", "M8", "M9", "M10"],
            vm.Macros.Select(m => m.ToolbarLabel));
        Assert.Equal("Macro 5", vm.Macros[3].Name);
        Assert.Equal("F5", vm.Macros[3].ShortcutKey);
    }

    [Fact]
    public void Duplicate_or_out_of_range_slots_from_a_hand_edited_file_are_repaired()
    {
        var settings = _settings.Load();
        settings.ModuleSettings["Macros"] = new List<Dictionary<string, object>>
        {
            new() { ["Name"] = "A", ["Code"] = "G28", ["Slot"] = 0 },
            new() { ["Name"] = "B", ["Code"] = "G28", ["Slot"] = 0 },   // duplicate
            new() { ["Name"] = "C", ["Code"] = "G28", ["Slot"] = 99 },  // out of range
        };

        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));

        Assert.Equal(["F1", "F2", "F3"], vm.Macros.Select(m => m.ShortcutKey));
    }

    [Fact]
    public void A_new_macro_is_named_after_the_slot_it_fills()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        vm.AddMacroCommand.Execute(null);
        vm.AddMacroCommand.Execute(null);
        vm.AddMacroCommand.Execute(null);
        vm.SelectedMacro = vm.Macros[1];
        vm.RemoveMacroCommand.Execute(null);   // frees F2

        vm.AddMacroCommand.Execute(null);

        Assert.Equal("Macro 2", vm.Macros[^1].Name);
        Assert.Equal("F2", vm.Macros[^1].ShortcutKey);
    }

    [Fact]
    public void Shortcut_labels_follow_list_order_when_slots_are_first_assigned()
    {
        var first = new MacroViewModel(_settings, new RecordingGrbl([]));
        first.Macros.Add(new MacroItem { Name = "Home", Code = "G28" });
        first.Macros.Add(new MacroItem { Name = "Probe", Code = "G38.2 Z-10" });
        first.Save();

        var second = new MacroViewModel(_settings, new RecordingGrbl([]));

        Assert.Equal(["F1", "F2"], second.Macros.Select(m => m.ShortcutKey));
    }

    [Fact]
    public void Short_name_is_optional_and_round_trips()
    {
        var first = new MacroViewModel(_settings, new RecordingGrbl([]));
        first.Macros.Add(new MacroItem { Name = "Home", Code = "G28", ShortName = "HOM" });
        first.Macros.Add(new MacroItem { Name = "Probe", Code = "G38.2 Z-10" });
        first.Save();

        var second = new MacroViewModel(_settings, new RecordingGrbl([]));

        Assert.Equal("HOM", second.Macros[0].ShortName);
        Assert.True(second.Macros[0].HasShortName);
        Assert.Equal("HOM", second.Macros[0].ToolbarLabel);
        Assert.Equal("1", second.Macros[0].ToolbarIndex);

        Assert.Equal(string.Empty, second.Macros[1].ShortName);
        Assert.False(second.Macros[1].HasShortName);
        Assert.Equal("M2", second.Macros[1].ToolbarLabel);

        // A whitespace-only name is not a name: the button keeps its M label.
        second.Macros[0].ShortName = "   ";
        Assert.False(second.Macros[0].HasShortName);
        Assert.Equal("M1", second.Macros[0].ToolbarLabel);
    }

    [Fact]
    public async Task Shortcut_run_sends_immediately_without_a_prompt()
    {
        var sent = new List<string>();
        var vm = new MacroViewModel(_settings, new RecordingGrbl(sent));
        vm.Macros.Add(new MacroItem { Name = "Home", Code = "G28\nG0 X0 Y0" });

        Assert.True(await vm.RunFromShortcutAsync(0));
        Assert.Equal(["G28", "G0 X0 Y0"], sent);
    }

    [Fact]
    public async Task Shortcut_run_ignores_a_missing_slot_and_reports_an_empty_macro()
    {
        var sent = new List<string>();
        var vm = new MacroViewModel(_settings, new RecordingGrbl(sent));
        vm.Macros.Add(new MacroItem { Name = "Empty", Code = "   " });

        Assert.False(await vm.RunFromShortcutAsync(0));
        Assert.Equal("This macro has no code", vm.Status);

        // No macro owns F6: the key is not claimed, so nothing is said either.
        Assert.False(await vm.RunFromShortcutAsync(5));
        Assert.Equal("This macro has no code", vm.Status);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task A_successful_run_clears_the_last_refusal()
    {
        var sent = new List<string>();
        var grbl = new StateGrbl("Hold:1", sent);
        var vm = new MacroViewModel(_settings, grbl);
        vm.Macros.Add(new MacroItem { Name = "Home", Code = "G28" });

        Assert.False(await vm.RunFromShortcutAsync(0));
        Assert.Equal("Wait for the machine to stop", vm.Status);

        grbl.Push("Idle");

        Assert.True(await vm.RunFromShortcutAsync(0));
        Assert.Equal(["G28"], sent);
        Assert.Equal(string.Empty, vm.Status);
    }

    [Fact]
    public async Task Running_an_empty_macro_from_the_editor_says_why()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        var macro = new MacroItem { Name = "Empty" };
        vm.Macros.Add(macro);

        await vm.RunAsync(macro);   // public: RunCommand.Execute can reach this

        Assert.Equal("This macro has no code", vm.Status);
    }

    [Theory]
    [InlineData("Run")]
    [InlineData("Hold")]
    [InlineData("Hold:0")]
    [InlineData("Hold:1")]
    [InlineData("Door")]
    [InlineData("Door:0")]
    [InlineData("Door:1")]
    [InlineData("Jog")]
    [InlineData("Home")]
    public async Task A_moving_or_held_machine_refuses_every_run_path(string status)
    {
        var sent = new List<string>();
        var vm = new MacroViewModel(_settings, new StateGrbl(status, sent));
        var macro = new MacroItem { Name = "Home", Code = "G28" };
        vm.Macros.Add(macro);
        vm.SelectedMacro = macro;

        Assert.True(vm.IsMachineBusy);
        Assert.False(vm.CanRunMacros);
        Assert.False(vm.RunCommand.CanExecute(null));
        Assert.False(vm.RunToolbarCommand.CanExecute(macro));

        Assert.False(await vm.RunFromShortcutAsync(0));
        await vm.RunAsync(macro);                       // public: the guard must hold
        Assert.DoesNotContain("G28", sent);
        Assert.Equal("Wait for the machine to stop", vm.Status);
    }

    [Fact]
    public async Task An_idle_machine_runs_from_the_toolbar_without_a_prompt()
    {
        var sent = new List<string>();
        var vm = new MacroViewModel(_settings, new StateGrbl("Idle", sent));
        var macro = new MacroItem { Name = "Home", Code = "G28" };
        vm.Macros.Add(macro);

        Assert.True(vm.CanRunMacros);
        Assert.True(vm.RunToolbarCommand.CanExecute(macro));
        vm.RunToolbarCommand.Execute(macro);
        await Task.Yield();

        Assert.Equal(["G28"], sent);
    }

    [Fact]
    public void Typing_code_into_an_empty_macro_enables_its_toolbar_button()
    {
        var vm = new MacroViewModel(_settings, new StateGrbl("Idle", []));
        var macro = new MacroItem { Name = "New" };
        vm.Macros.Add(macro);

        Assert.False(vm.RunToolbarCommand.CanExecute(macro));

        macro.Code = "G28";

        Assert.True(vm.RunToolbarCommand.CanExecute(macro));
    }

    [Fact]
    public void One_macro_mutation_writes_the_profile_once()
    {
        var writes = 0;
        _settings.Changed += () => writes++;

        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        vm.AddMacroCommand.Execute(null);          // add + save
        Assert.Equal(1, writes);

        vm.Macros[0].Name = "Renamed";             // one edit, one write
        Assert.Equal(2, writes);

        vm.Macros[0].ShortName = "REN";
        Assert.Equal(3, writes);
    }

    [Fact]
    public void Loading_a_profile_does_not_rewrite_it()
    {
        var first = new MacroViewModel(_settings, new RecordingGrbl([]));
        for (var i = 0; i < 12; i++)
            first.Macros.Add(new MacroItem { Name = $"Macro {i + 1}", Code = "G28" });
        first.Save();

        var writes = 0;
        _settings.Changed += () => writes++;

        var second = new MacroViewModel(_settings, new RecordingGrbl([]));

        Assert.Equal(12, second.Macros.Count);
        Assert.Equal(0, writes);                   // renumbering is derived state
    }

    [Fact]
    public void Filling_an_empty_macro_updates_only_that_toolbar_item()
    {
        var vm = new MacroViewModel(_settings, new RecordingGrbl([]));
        vm.Macros.Add(new MacroItem { Name = "A" });              // no code: no button
        vm.Macros.Add(new MacroItem { Name = "B", Code = "G28" });

        var actions = new List<NotifyCollectionChangedAction>();
        vm.ToolbarMacros.CollectionChanged += (_, e) => actions.Add(e.Action);

        vm.Macros[0].Code = "G0 X0";

        Assert.Equal(["A", "B"], vm.ToolbarMacros.Select(m => m.Name));
        Assert.Equal([NotifyCollectionChangedAction.Add], actions);  // in place, no Reset

        actions.Clear();
        vm.Macros[0].Code = string.Empty;

        Assert.Equal(["B"], vm.ToolbarMacros.Select(m => m.Name));
        Assert.Equal([NotifyCollectionChangedAction.Remove], actions);
    }

    [Fact]
    public void The_key_table_is_the_one_source_of_slot_count_and_keys()
    {
        Assert.Equal(MacroShortcutKeys.Count, MacroViewModel.ShortcutCount);
        Assert.Equal(HotkeyGrammar.MacroShortcutGestures.Count, MacroShortcutKeys.Count);
        Assert.Equal(0, MacroShortcutKeys.IndexOf(Avalonia.Input.Key.F1));
        Assert.Equal(4, MacroShortcutKeys.IndexOf(Avalonia.Input.Key.F5));
        Assert.Equal(11, MacroShortcutKeys.IndexOf(Avalonia.Input.Key.F12));
        Assert.Equal(-1, MacroShortcutKeys.IndexOf(Avalonia.Input.Key.A));
    }

    private sealed class StateGrbl(string status, List<string> sent) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3) { Status = status });
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) { sent.Add(line); return Task.CompletedTask; }
        public void Push(string status) => _subject.OnNext(new MachineState(3) { Status = status });
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
        Assert.False(await vm.RunFromShortcutAsync(0)); // the shortcut path refuses too
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
