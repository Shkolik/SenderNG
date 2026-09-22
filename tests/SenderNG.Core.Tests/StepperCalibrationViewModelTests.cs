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

using System.Globalization;
using System.Reactive.Subjects;
using CNC.Core;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Core.Models;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class StepperCalibrationViewModelTests
{
    private sealed class RecordingGrbl : IGrblService
    {
        public List<string> Lines { get; } = [];
        public List<byte> Realtime { get; } = [];
        public ModalState? Modal { get; set; }
        public Func<string, Task>? OnSend { get; set; }
        public MachineState Current { get; } = new(3) { Status = "Idle", IsHomed = true };
        public ControllerCapabilities Capabilities { get; init; } = new() { NumAxes = 3 };
        public bool IsConnected => true;

        // BehaviorSubject, not Subject: exactly what DemoGrblService/
        // GrblServiceProxy expose in production, and the object the wizard's
        // idle gate sees. The gate subscribes *before* the send and drops the
        // subscribe-time replay, so the fake must emit the post-move state
        // during SendAsync the way a real controller's poller eventually does.
        private readonly BehaviorSubject<MachineState> _state;
        public IObservable<MachineState> State => _state;

        public RecordingGrbl() => _state = new BehaviorSubject<MachineState>(Current);

        // When set, SimulateMotion mutates the position without emitting: hosts
        // the C1 regression test, which streams status updates on its own to
        // mimic a poller that reports Run only while the axis actually moves.
        public bool DeferMotionEmit { get; set; }

        public void EmitStatus(string status)
        {
            Current.Status = status;
            _state.OnNext(Current);
        }

        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) => Realtime.Add(code);
        public Task SendAsync(string line)
        {
            Lines.Add(line);
            SimulateMotion(line);
            return OnSend?.Invoke(line) ?? Task.CompletedTask;
        }
        public Task<ModalState?> ReadModalStateAsync() => Task.FromResult(Modal);
        public void Emit() => _state.OnNext(Current);

        // The real controller advances its position as the move executes, and the
        // wizard's return leg bases its space check on where the forward leg left
        // the axis — so the fake has to move too. It re-emits Run then Idle, the
        // way a poller sees the axis travel then finish.
        private void SimulateMotion(string line)
        {
            var g0 = line.IndexOf("G0", StringComparison.Ordinal);
            if (g0 < 0 || g0 + 2 >= line.Length)
                return;
            var index = "XYZ".IndexOf(line[g0 + 2]);
            if (index < 0 ||
                !double.TryParse(line[(g0 + 3)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
                return;
            if (index < Current.Positions.Length)
                Current.Positions[index] += delta;
            if (Current.MachinePositions is { } machine && index < machine.Length)
                machine[index] += delta;
            if (!DeferMotionEmit)
            {
                Current.Status = "Run";
                _state.OnNext(Current);
                Current.Status = "Idle";
                _state.OnNext(Current);
            }
        }
    }

    private sealed class StubProvider : IGrblSettingsProvider
    {
        public IReadOnlyList<string> ValueLines { get; init; } = [];
        public Task<IReadOnlyList<string>> GetSettingLinesAsync() => Task.FromResult(ValueLines);
        public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> GetSettingDescriptionAsync(int id) => Task.FromResult<string?>(null);
    }

    private static readonly LocalizationService Loc = SenderNG.Core.Tests.TestLocalization.Locale;

    private static (StepperCalibrationViewModel Vm, RecordingGrbl Grbl, StubProvider Provider)
        Create(bool isHal = false, bool backlashSetting = true, bool homed = true)
    {
        var lines = new List<string>
        {
            "$100=250.000", "$130=200.000",
        };
        if (backlashSetting)
            lines.Add("$160=0.000");
        var provider = new StubProvider { ValueLines = lines };
        var grbl = new RecordingGrbl();
        grbl.Current.IsHomed = homed;
        var info = new GrblInfoState { NumAxes = 3, IsGrblHAL = isHal };
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), $"stepper-{Guid.NewGuid():N}"));
        var vm = new StepperCalibrationViewModel(grbl, provider, info, settings, Loc);
        return (vm, grbl, provider);
    }

    [Fact]
    public async Task Computes_new_steps_per_unit_with_classic_rounding()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync();
        vm.Distance = 100;
        vm.MeasuredDistance = 90;
        Assert.Equal(277.778, vm.NewStepsPerUnit!.Value, 3);
    }

    [Fact]
    public async Task Computes_new_steps_per_unit_with_hal_rounding()
    {
        var (vm, _, _) = Create(isHal: true);
        await vm.InitializeAsync();
        vm.Distance = 100;
        vm.MeasuredDistance = 90;
        Assert.Equal(277.777778, vm.NewStepsPerUnit!.Value, 6);
    }

    [Fact]
    public async Task Computes_new_steps_from_the_absolute_distance()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync();
        vm.Distance = -100;
        vm.MeasuredDistance = 90;
        Assert.Equal(277.778, vm.NewStepsPerUnit!.Value, 3);
    }

    [Fact]
    public async Task Negative_distance_moves_in_the_negative_direction()
    {
        var (vm, grbl, _) = Create();
        await vm.InitializeAsync();
        grbl.Current.MachinePositions![0] = 100;   // allow the -100 move inside $130=200
        grbl.Current.Positions[0] = 100;
        vm.Distance = -100;
        await vm.NextAsync();
        Assert.Equal(StepperCalibrationStep.MeasureForward, vm.Step);
        Assert.Contains(grbl.Lines, l => l == "G21G91G0X-100");

        vm.MeasuredDistance = 100;
        await vm.NextAsync();                      // return leg comes back +100
        Assert.Equal(StepperCalibrationStep.MeasureBacklash, vm.Step);
        Assert.Contains(grbl.Lines, l => l == "G21G91G0X100");
    }

    [Fact]
    public async Task Prefills_backlash_when_reaching_the_measure_backlash_step()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync();
        vm.MeasuredDistance = 100;
        await vm.NextAsync();               // forward
        Assert.Null(vm.Backlash);
        await vm.NextAsync();               // return -> MeasureBacklash
        Assert.Equal(StepperCalibrationStep.MeasureBacklash, vm.Step);
        Assert.Equal(0d, vm.Backlash);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public async Task Resume_after_stop_keeps_the_entered_backlash()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync();
        vm.MeasuredDistance = 100;
        await vm.NextAsync();
        await vm.NextAsync();
        vm.Backlash = 0.25;
        await vm.StopAsync();            // abort back to Setup; values are kept so the user can resume
        Assert.Equal(StepperCalibrationStep.Setup, vm.Step);

        await vm.NextAsync();            // re-run from Setup
        await vm.NextAsync();
        Assert.Equal(StepperCalibrationStep.MeasureBacklash, vm.Step);
        Assert.Equal(0.25, vm.Backlash); // the 0 prefill must not clobber a value already entered
    }

    [Fact]
    public async Task Advances_through_the_state_machine()
    {
        var (vm, grbl, _) = Create();
        await vm.InitializeAsync();
        Assert.True(vm.CanAdvance);

        await vm.NextAsync();
        Assert.Equal(StepperCalibrationStep.MeasureForward, vm.Step);

        vm.MeasuredDistance = 100;
        await vm.NextAsync();
        Assert.Equal(StepperCalibrationStep.MeasureBacklash, vm.Step);

        vm.Backlash = 0;
        Assert.True(vm.CanSave);
        await vm.SaveAsync();
        Assert.Equal(StepperCalibrationStep.Done, vm.Step);
        // forward, return, $100, $160 — no modal restore (this fake exposes no modal state)
        Assert.Equal(4, grbl.Lines.Count);
    }

    [Fact]
    public async Task Blocks_a_move_that_leaves_the_travel_envelope()
    {
        var (vm, grbl, _) = Create();
        await vm.InitializeAsync();
        grbl.Current.MachinePositions![0] = 190;   // homed: +100 would exceed $130=200
        await vm.NextAsync();
        Assert.Equal(StepperCalibrationStep.Setup, vm.Step);
        Assert.Empty(grbl.Lines);
        Assert.Contains("exceeds", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Warns_and_skips_the_check_when_travel_is_unknown()
    {
        var (vm, grbl, _) = Create(homed: false);
        await vm.InitializeAsync();
        await vm.NextAsync();
        Assert.Equal(StepperCalibrationStep.MeasureForward, vm.Step);
        Assert.Contains("G21G91G0X100", grbl.Lines);
        Assert.Contains("space check", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writes_backlash_only_when_the_setting_exists()
    {
        var (vm, grbl, _) = Create(backlashSetting: true);
        await vm.InitializeAsync();
        vm.MeasuredDistance = 100;
        await vm.NextAsync();               // forward
        await vm.NextAsync();               // return
        vm.Backlash = 0.25;
        await vm.SaveAsync();
        Assert.Contains(grbl.Lines, l => l.StartsWith("$100="));
        Assert.Contains(grbl.Lines, l => l == "$160=0.25");
        Assert.True(vm.BacklashSupported);
        Assert.Contains("Calibration saved", vm.Status);
        Assert.Contains("Steps/mm: 250", vm.Status);
        Assert.Contains("backlash 0.25 mm saved", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Skips_backlash_and_disclaims_when_the_setting_is_absent()
    {
        var (vm, grbl, _) = Create(backlashSetting: false);
        await vm.InitializeAsync();
        vm.MeasuredDistance = 100;
        await vm.NextAsync();
        await vm.NextAsync();
        vm.Backlash = 0.25;
        await vm.SaveAsync();
        Assert.DoesNotContain(grbl.Lines, l => l.StartsWith("$160="));
        Assert.False(vm.BacklashSupported);
        Assert.Contains("Calibration saved", vm.Status);
        Assert.Contains("not supported", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restores_modal_state_after_save()
    {
        var (vm, grbl, _) = Create();
        grbl.Modal = new ModalState(false, false);   // G20 + G91
        await vm.InitializeAsync();
        vm.MeasuredDistance = 100;
        await vm.NextAsync();
        await vm.NextAsync();
        vm.Backlash = 0;
        await vm.SaveAsync();
        Assert.Equal(new[] { "G20", "G91" }, grbl.Lines.TakeLast(2));
    }

    [Fact]
    public async Task Stops_with_the_firmware_byte_during_a_move()
    {
        var (vm, grbl, _) = Create(isHal: true);
        await vm.InitializeAsync();
        var release = new TaskCompletionSource();
        grbl.OnSend = _ => release.Task;

        var move = vm.NextAsync();          // sets IsMoving, awaits the move
        Assert.True(vm.IsMoving);
        await vm.StopAsync();
        Assert.Equal(new[] { GrblRealtime.Stop }, grbl.Realtime);
        Assert.Equal(StepperCalibrationStep.Setup, vm.Step);

        release.SetResult();
        await move;
    }

    [Fact]
    public async Task Classic_stop_sends_reset_during_a_move()
    {
        var (vm, grbl, _) = Create(isHal: false);
        await vm.InitializeAsync();
        var release = new TaskCompletionSource();
        grbl.OnSend = _ => release.Task;

        var move = vm.NextAsync();
        await vm.StopAsync();
        Assert.Equal(new[] { GrblRealtime.Reset }, grbl.Realtime);

        release.SetResult();
        await move;
    }

    [Fact]
    public async Task Waits_for_a_real_idle_after_the_move_not_the_premove_replay()
    {
        var (vm, grbl, _) = Create();
        grbl.DeferMotionEmit = true;   // no post-move emission during SendAsync
        await vm.InitializeAsync();

        var move = vm.NextAsync();
        // The command was sent, but with the poller still reporting the pre-move
        // "Idle" the wizard must NOT advance — the subscribe-time replay no longer
        // fakes a completed move (C1 regression).
        Assert.Contains(grbl.Lines, l => l.StartsWith("G21G91G0X"));
        Assert.Equal(StepperCalibrationStep.Setup, vm.Step);
        Assert.True(vm.IsMoving);

        // The poller sees the axis move, then finish.
        grbl.EmitStatus("Run");
        grbl.EmitStatus("Idle");
        await move;
        Assert.Equal(StepperCalibrationStep.MeasureForward, vm.Step);
    }

    [Fact]
    public async Task Dialog_close_restores_modal_state_when_interrupted()
    {
        var (vm, grbl, _) = Create();
        grbl.Modal = new ModalState(false, false);   // G20 + G91
        await vm.InitializeAsync();
        vm.MeasuredDistance = 100;
        await vm.NextAsync();                        // mid-wizard: modal G91 left active
        await vm.OnDialogClosedAsync();
        Assert.Equal(new[] { "G20", "G91" }, grbl.Lines.TakeLast(2));
        Assert.Equal(StepperCalibrationStep.Setup, vm.Step);
    }
}