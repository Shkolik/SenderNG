/*
 * StepperCalibrationViewHeadlessTests.cs - headless UI tests for the stepper
 * calibration wizard (button enablement driven from the ViewModel).
 */

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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CNC.Core;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Tests;

public class StepperCalibrationViewHeadlessTests
{
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private sealed class RecordingGrbl : IGrblService
    {
        public List<string> Lines { get; } = [];
        public ControllerCapabilities Capabilities { get; init; } = new() { NumAxes = 3 };
        public bool IsConnected => true;

        private readonly BehaviorSubject<MachineState> _state;
        public IObservable<MachineState> State => _state;

        public RecordingGrbl() =>
            _state = new BehaviorSubject<MachineState>(new MachineState(3) { Status = "Idle", IsHomed = true });

        public void EmitStatus(string status)
        {
            var current = _state.Value;
            current.Status = status;
            _state.OnNext(current);
        }

        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line)
        {
            Lines.Add(line);
            var g0 = line.IndexOf("G0", StringComparison.Ordinal);
            if (g0 >= 0 && g0 + 2 < line.Length &&
                double.TryParse(line[(g0 + 3)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
            {
                var index = "XYZ".IndexOf(line[g0 + 2]);
                var current = _state.Value;
                if (index >= 0 && index < current.Positions.Length)
                    current.Positions[index] += delta;
                if (current.MachinePositions is { Length: > 0 } machine && index < machine.Length)
                    machine[index] += delta;
                current.Status = "Run";
                _state.OnNext(current);
                current.Status = "Idle";
                _state.OnNext(current);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class StubProvider : IGrblSettingsProvider
    {
        public IReadOnlyList<string> ValueLines { get; init; } = [];
        public Task<IReadOnlyList<string>> GetSettingLinesAsync() => Task.FromResult(ValueLines);
        public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> GetSettingDescriptionAsync(int id) => Task.FromResult<string?>(null);
    }

    private static (StepperCalibrationViewModel Vm, RecordingGrbl Grbl) Create()
    {
        var provider = new StubProvider
        {
            ValueLines = ["$100=250.000", "$130=200.000", "$160=0.000"],
        };
        var grbl = new RecordingGrbl();
        var info = new GrblInfoState { NumAxes = 3, IsGrblHAL = false };
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), $"stepper-ui-{Guid.NewGuid():N}"));
        var vm = new StepperCalibrationViewModel(grbl, provider, info, settings, Loc);
        return (vm, grbl);
    }

    private static async Task AdvanceToBacklashAsync(StepperCalibrationViewModel vm)
    {
        await vm.InitializeAsync();
        await vm.NextAsync();               // Setup -> MeasureForward (moves +100)
        await Pump();
        vm.MeasuredDistance = 100;
        await vm.NextAsync();               // MeasureForward -> MeasureBacklash (moves -100)
        await Pump();
    }

    [AvaloniaFact]
    public async Task Wizard_buttons_follow_computed_enable_properties()
    {
        var (vm, _) = Create();
        var view = new StepperCalibrationView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var save = window.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.Command, vm.SaveCommand));
        var next = window.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.Command, vm.NextCommand));
        var stop = window.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.Command, vm.StopCommand));

        // Attach-time state (Setup): Next shown, Save/Stop off.
        Assert.True(next.IsVisible);
        Assert.False(save.IsEnabled);
        Assert.False(stop.IsEnabled);

        await AdvanceToBacklashAsync(vm);
        Assert.Equal(StepperCalibrationStep.MeasureBacklash, vm.Step);

        // At the measure-backlash step: Next spent, and the field is prefilled
        // with 0 so Save is immediately available.
        Assert.False(vm.CanAdvance);
        Assert.False(next.IsVisible);
        Assert.True(vm.CanSave);
        Assert.True(save.IsEnabled, "Save stays disabled at the backlash step despite the 0 prefill.");

        // Clearing the field must disable Save again — the exact property
        // notification that once left the binding stale.
        vm.Backlash = null;
        await Pump();
        Assert.False(vm.CanSave);
        Assert.False(save.IsEnabled, "Save button still enabled after the backlash field was cleared.");

        // And entering a value re-enables it.
        vm.Backlash = 0.25;
        await Pump();
        Assert.True(vm.CanSave);
        Assert.True(save.IsEnabled, "Save button is still disabled after the user entered a backlash value.");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Measured_and_backlash_reject_the_minus_sign()
    {
        var (vm, _) = Create();
        var view = new StepperCalibrationView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var distance = window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Name == "DistanceBox");
        var measured = window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Name == "MeasuredBox");
        var backlash = window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Name == "BacklashBox");

        // The minus sign is legal for the commanded distance (negative moves): the
        // TextBox accepts it and the "-" lands in the field.
        distance.Text = string.Empty;   // clear the pre-filled "100" like a fresh entry would
        distance.CaretIndex = 0;
        RaiseSign(distance);
        Assert.Equal("-", distance.Text);

        // ...but must never reach the measured distance / backlash inputs,
        // where it can only produce a transient binding validation error.
        AssertBlocked(measured, "-");
        AssertBlocked(backlash, "-");
        AssertBlocked(backlash, "-5");
        window.Close();
    }

    private static void AssertBlocked(TextBox box, string input)
    {
        box.Text = string.Empty;
        box.CaretIndex = 0;
        RaiseText(box, input);
        Assert.Equal(string.Empty, box.Text);
    }

    private static void RaiseSign(TextBox box) => RaiseText(box, "-");

    private static void RaiseText(TextBox box, string input)
    {
        var evt = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = input };
        box.RaiseEvent(evt);
    }

    /// <summary>Yield so the headless SynchronizationContext runs pending layout/render.</summary>
    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}