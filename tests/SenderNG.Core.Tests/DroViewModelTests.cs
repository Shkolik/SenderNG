
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
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using SenderNG.Localization;
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class DroViewModelTests
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
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public void State_push_shows_axis_letters_and_positions()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new DroViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState { NumAxes = 3, Status = "Idle", Positions = [1, 2, 3] });

        Assert.Equal(3, vm.Axes.Count);
        Assert.Equal("X", vm.Axes[0].Letter);
        Assert.Equal("Y", vm.Axes[1].Letter);
        Assert.Equal("Z", vm.Axes[2].Letter);
        Assert.Equal(1, vm.Axes[0].Position);
        Assert.Equal(2, vm.Axes[1].Position);
        Assert.Equal(3, vm.Axes[2].Position);
    }

    [Fact]
    public void NumAxes_drives_row_count()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new DroViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState(4));

        Assert.Equal(4, vm.Axes.Count);
        Assert.Equal("A", vm.Axes[3].Letter);

        subject.OnNext(new MachineState(2));

        Assert.Equal(2, vm.Axes.Count);
    }

    [Fact]
    void Initial_subject_value_is_applied_on_construction()
    {
        var subject = new BehaviorSubject<MachineState>(
            new MachineState { NumAxes = 3, Positions = [5, 6, 7] });
        var vm = new DroViewModel(new BehaviorSubjectGrbl(subject));

        Assert.Equal(3, vm.Axes.Count);
        Assert.Equal(5, vm.Axes[0].Position);
    }

    private sealed class RecordingGrbl : IGrblService
    {
        public List<string> Sent { get; } = new();
        public int RefreshCount { get; private set; }
        public bool IsConnected => true;
        public IObservable<MachineState> State { get; } = new BehaviorSubject<MachineState>(new MachineState(3));
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) { Sent.Add(line); return Task.CompletedTask; }
        public Task RefreshWorkParametersAsync() { RefreshCount++; return Task.CompletedTask; }
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public async Task Zero_axis_writes_active_work_offset_instead_of_G92()
    {
        // Regression: the DRO zero must persist into the active work offset
        // (G10 L20 P0, iSender parity) — G92 only left a temporary shift that
        // never touched the offsets table.
        var grbl = new RecordingGrbl();
        var vm = new DroViewModel(grbl);

        vm.ZeroAxisCommand.Execute("X");
        await Task.Delay(50);

        Assert.Equal("G10 L20 P0 X0", Assert.Single(grbl.Sent));
    }

    [Fact]
    public async Task Zero_all_writes_active_work_offset_instead_of_G92()
    {
        var grbl = new RecordingGrbl();
        var vm = new DroViewModel(grbl);

        vm.ZeroAllCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal("G90 G10 L20 P0 X0 Y0 Z0", Assert.Single(grbl.Sent));
    }

    [Fact]
    public async Task Zero_pulls_back_work_parameters_so_the_offsets_table_syncs()
    {
        // After the zero write the app must $# re-read — otherwise the saved
        // offset never reaches the Work Parameters table (the controller only
        // answers explicit queries, it doesn't broadcast dumps on its own).
        var grbl = new RecordingGrbl();
        var vm = new DroViewModel(grbl);

        vm.ZeroAllCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(1, grbl.RefreshCount);
    }

    [Fact]
    public void Zero_cell_tip_shows_the_bound_gesture_and_tracks_remap()
    {
        var previousHotkeys = AppHotkeys.Current;
        try
        {
            var settings = new SettingsService(
                Path.Combine(Path.GetTempPath(), "senderng-dro-" + Guid.NewGuid().ToString("N")));
            var hotkeys = new HotkeyService(settings);
            hotkeys.RegisterAction(
                new CommandHotkeyAction("dro.zero.x", "Zero X", null, () => { }), "Ctrl+Shift+X");
            hotkeys.RegisterAction(
                new CommandHotkeyAction("dro.zero.z", "Zero Z", null, () => { }), "Ctrl+Shift+Z");
            AppHotkeys.Current = hotkeys;

            var vm = new DroViewModel(new RecordingGrbl());

            Assert.Equal("Zero axis [Ctrl+Shift+X]", vm.Axes[0].Tip);
            Assert.Equal("Zero axis [Ctrl+Shift+Z]", vm.Axes[2].Tip);

            hotkeys.ClearGesture("dro.zero.x");
            Assert.Equal("Zero axis", vm.Axes[0].Tip);

            hotkeys.SetGesture("dro.zero.x", "Ctrl+Shift+D9");
            Assert.Equal("Zero axis [Ctrl+Shift+D9]", vm.Axes[0].Tip);
        }
        finally
        {
            AppHotkeys.Current = previousHotkeys;
        }
    }

    [Fact]
    public void Replacing_the_hotkey_service_refreshes_once_and_releases_the_previous_one()
    {
        var previousHotkeys = AppHotkeys.Current;
        try
        {
            var settings = new SettingsService(
                Path.Combine(Path.GetTempPath(), "senderng-dro-" + Guid.NewGuid().ToString("N")));
            var first = new HotkeyService(settings);
            first.RegisterAction(
                new CommandHotkeyAction("dro.zero.x", "Zero X", null, () => { }), "Ctrl+Shift+X");
            AppHotkeys.Current = first;

            var vm = new DroViewModel(
                new BehaviorSubjectGrbl(new BehaviorSubject<MachineState>(new MachineState(3))));

            var tips = 0;
            vm.Axes[0].PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Tip")
                    tips++;
            };

            first.ClearGesture("dro.zero.x");
            Assert.Equal(1, tips);   // sanity: the handler is live

            // A second service over a SEPARATE settings store: belt-and-braces
            // isolation, so the two services' gesture maps stay independent and the
            // only thing linking `replacement` to `first` is AppHotkeys.Current --
            // which is the mechanism under test. (It is not there to stop a
            // Persist() from reaching `first`: RegisterAction never calls Persist()
            // and never raises Changed -- only SetGesture, ResetOne and ResetAll
            // do (HotkeyService.cs:118, :129, :138).)
            var replacement = new HotkeyService(new SettingsService(
                Path.Combine(Path.GetTempPath(), "senderng-dro-" + Guid.NewGuid().ToString("N"))));
            replacement.RegisterAction(
                new CommandHotkeyAction("dro.zero.x", "Zero X", null, () => { }), "Ctrl+Shift+Y");

            // Swap away and back. The round trip is what makes the duplication
            // reachable: AppHotkeys.Current is reference-guarded, so a service can
            // only gain a second subscription by being handed out a second time.
            var beforeSwap = tips;
            AppHotkeys.Current = replacement;
            AppHotkeys.Current = first;
            Assert.True(tips > beforeSwap, "sanity: the round trip raised AppHotkeys.Changed");
            tips = 0;

            first.ClearGesture("dro.zero.x");
            Assert.Equal(1, tips);   // two before the fix: the round trip stacked a copy

            tips = 0;
            replacement.ClearGesture("dro.zero.x");
            Assert.Equal(0, tips);   // one before the fix: the replaced service was never released
        }
        finally
        {
            AppHotkeys.Current = previousHotkeys;
        }
    }

    [Fact]
    public void Zero_cell_tip_has_no_brackets_for_axes_without_an_action()
    {
        var previousHotkeys = AppHotkeys.Current;
        try
        {
            AppHotkeys.Current = new HotkeyService(new SettingsService(
                Path.Combine(Path.GetTempPath(), "senderng-dro-" + Guid.NewGuid().ToString("N"))));
            var vm = new DroViewModel(new RecordingGrbl());
            Assert.Equal("Zero axis", vm.Axes[0].Tip);
        }
        finally
        {
            AppHotkeys.Current = previousHotkeys;
        }
    }
}

public class DroProxyInitialStateTests
{
    private sealed class FakeGrbl(BehaviorSubject<MachineState> subject) : IGrblService
    {
        public bool IsConnected => true;
        public IObservable<MachineState> State => subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public void Axes_populate_immediately_through_proxy()
    {
        // Regression: startup wiring routes the demo service through
        // GrblServiceProxy; its State must replay the current machine state,
        // otherwise the DRO shows no axis rows until the first real emission.
        var inner = new FakeGrbl(new BehaviorSubject<MachineState>(new MachineState(6)));
        var proxy = new GrblServiceProxy(inner);

        var vm = new DroViewModel(proxy);

        Assert.Equal(6, vm.Axes.Count);
    }
}
