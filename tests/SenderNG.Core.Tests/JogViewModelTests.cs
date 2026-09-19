
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
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class JogViewModelTests
{
    internal static SettingsService MakeSettings(string mode = "Mill")
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var app = settings.Load();
        app.MachineTypeId = mode;
        settings.Save(app);
        return settings;
    }

    internal sealed class RecordingGrbl(List<string> sent, int numAxes = 4,
        ControllerCapabilities? capabilities = null) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(numAxes));
        public bool IsConnected { get; set; } = true;
        public ControllerCapabilities Capabilities => capabilities ?? ControllerCapabilities.Unknown;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) => sent.Add($"RT:{code:X2}");
        
public Task SendAsync(string line)
        {
            sent.Add(line);
            return Task.CompletedTask;
        }
        public void Push(MachineState state) => _subject.OnNext(state);
    }

    [Fact]
    public void Default_distances_speeds_and_indexes_are_populated()
    {
        var vm = new JogViewModel(new RecordingGrbl([]), MakeSettings());

        Assert.Equal([0.1, 1.0, 10.0, -1], vm.Config.Distances);
        Assert.Equal([500.0, 1000.0, 2000.0], vm.Config.Speeds);
        Assert.Equal(1, vm.SelectedDistanceIndex);
        Assert.Equal(1, vm.SelectedSpeedIndex);
        Assert.Equal(1.0, vm.SelectedDistance);
        Assert.Equal(1000.0, vm.SelectedSpeed);
    }

    [Fact]
    public void Mill_axis_jog_applies_selected_distance()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());
        vm.IsDistance2 = true; // 10

        vm.JogCommand.Execute("X+");

        Assert.Equal(["$J=G91 X10 F1000"], sent);
    }

    [Fact]
    public void Mill_diagonal_sends_both_axes()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());
        vm.IsSpeed2 = true; // 2000

        vm.JogCommand.Execute("X-,Y+");

        Assert.Equal(["$J=G91 X-1 Y1 F2000"], sent);
    }

    [Fact]
    public void Foam_together_jogs_both_gantries_mirrored()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));
        vm.Together = true;

        vm.JogCommand.Execute("X-,Y+|Z-,A+");

        Assert.Equal(["$J=G91 X-1 Y1 Z-1 A1 F1000"], sent);
    }

    [Fact]
    public void Foam_not_together_ignores_second_pair()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());

        vm.JogCommand.Execute("X+,Y-|Z+,A-");

        Assert.Equal(["$J=G91 X1 Y-1 F1000"], sent);
    }

    [Fact]
    public void Together_lock_disables_ZA_cross()
    {
        var vm = new JogViewModel(new RecordingGrbl([]), MakeSettings("Foam"));

        Assert.True(vm.ZaCrossEnabled);
        vm.Together = true;
        Assert.False(vm.ZaCrossEnabled);
        vm.Together = false;
        Assert.True(vm.ZaCrossEnabled);
    }

    [Fact]
    public void Distance_selection_persists_and_reloads()
    {
        var settings = MakeSettings();
        var first = new JogViewModel(new RecordingGrbl([]), settings);
        first.IsDistance2 = true;
        first.IsSpeed0 = true;

        var second = new JogViewModel(new RecordingGrbl([]), settings);

        Assert.Equal(2, second.SelectedDistanceIndex);
        Assert.Equal(10.0, second.SelectedDistance);
        Assert.Equal(0, second.SelectedSpeedIndex);
        Assert.Equal(500.0, second.SelectedSpeed);
    }

    [Fact]
    public void Configs_are_mode_aware()
    {
        // Each machine keeps its own Jog section (the settings split replaced
        // the per-mode nesting with per-profile files).
        var settings = MakeSettings("Mill");
        var millVm = new JogViewModel(new RecordingGrbl([]), settings);
        millVm.Config.Distances[0] = 0.05;
        millVm.Save();

        var foamSettings = MakeSettings("Foam");
        var foamVm = new JogViewModel(new RecordingGrbl([]), foamSettings);

        Assert.Equal(0.1, foamVm.Config.Distances[0]); // foam machine untouched
        Assert.Equal(0.05, JogConfigStore.Load(settings.Load()).Distances[0]); // mill custom
    }

    [Fact]
    public void Linear_rows_track_machine_num_axes()
    {
        var grbl = new RecordingGrbl([], numAxes: 4);
        var vm = new JogViewModel(grbl, MakeSettings());

        Assert.Equal(["Z", "A"], vm.LinearAxisLetters.Select(r => r.Letter));

        grbl.Push(new MachineState(5));
        Assert.Equal(["Z", "A", "B"], vm.LinearAxisLetters.Select(r => r.Letter));
    }

    [Fact]
    public void Row_tags_combine_letter_and_sign()
    {
        var row = new LinearAxisRow("B");
        Assert.Equal("B+", row.PlusTag);
        Assert.Equal("B-", row.MinusTag);
    }

    [Fact]
    public void Stop_command_sends_jog_cancel_realtime_byte()
    {
        // Jog cancel (0x85) is the firmware's dedicated stop-jog command on classic
        // grbl 1.1, the mega5x fork and grblHAL: it returns to IDLE, while a feed
        // hold ('!') would leave the machine in HOLD.
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());

        vm.StopCommand.Execute(null);

        Assert.Equal(["RT:85"], sent);
    }

    [Fact]
    public void Continuous_step_is_selected_via_fourth_toggle()
    {
        var vm = new JogViewModel(new RecordingGrbl([]), MakeSettings());

        vm.IsDistance3 = true;

        Assert.True(vm.IsContinuousStep);
    }

    [Fact]
    public void Continuous_toggle_disabled_until_travel_known()
    {
        // Without a connect-time $$ fetch the board's per-axis travel is unknown:
        // the Continuous slot stays unavailable until MaxTravel reports values.
        var unknown = new JogViewModel(new RecordingGrbl([]), MakeSettings());
        Assert.False(unknown.ContinuousAvailable);

        var travel = new JogViewModel(new RecordingGrbl([],
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings());
        Assert.True(travel.ContinuousAvailable);
    }

    [Fact]
    public void Continuous_press_sends_one_travel_jog()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings());
        vm.IsDistance3 = true;

        vm.StartContinuousJog("X-");

        Assert.Equal(["$J=G91 X-200 F1000"], sent);
    }

    [Fact]
    public void Continuous_release_sends_only_jog_cancel()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings());
        vm.IsDistance3 = true;
        vm.StartContinuousJog("X-");
        Assert.Equal(["$J=G91 X-200 F1000"], sent);

        vm.StopContinuousJog();

        Assert.Equal(["$J=G91 X-200 F1000", "RT:85"], sent);
    }

    [Fact]
    public void Continuous_start_without_travel_sends_nothing()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());
        vm.IsDistance3 = true;

        vm.StartContinuousJog("X-");
        vm.StopContinuousJog();

        Assert.Empty(sent);
    }

    [Fact]
    public void Second_press_while_held_is_ignored()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings());
        vm.IsDistance3 = true;

        vm.StartContinuousJog("X-");
        vm.StartContinuousJog("Y+");

        Assert.Equal(["$J=G91 X-200 F1000"], sent);

        vm.StopContinuousJog();

        Assert.Equal(["$J=G91 X-200 F1000", "RT:85"], sent);
    }

    [Fact]
    public void Continuous_start_in_step_mode_sends_nothing()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings());

        vm.StartContinuousJog("X-");
        vm.StopContinuousJog();

        Assert.Empty(sent);
    }

    [Fact]
    public void Step_jog_command_is_ignored_while_continuous_held()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings());
        vm.IsDistance3 = true;
        vm.StartContinuousJog("X-");
        Assert.Equal(["$J=G91 X-200 F1000"], sent);

        vm.JogCommand.Execute("X+");

        Assert.Equal(["$J=G91 X-200 F1000"], sent);
    }

    [Fact]
    public void Step_jog_waits_for_connection()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] })
        { IsConnected = false }, MakeSettings());

        vm.JogCommand.Execute("X+");

        Assert.Empty(sent);
    }

    [Fact]
    public void Foam_together_continuous_jogs_both_gantries()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings("Foam"));
        vm.Together = true;
        vm.IsDistance3 = true;

        vm.StartContinuousJog("X-,Y+|Z-,A+");

        Assert.Equal(["$J=G91 X-200 Y300 Z-100 A50 F1000"], sent);
    }

    [Fact]
    public void Stop_button_clears_the_hold_and_sends_cancel()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings());
        vm.IsDistance3 = true;
        vm.StartContinuousJog("X-");
        Assert.True(vm.ContinuousActive);

        vm.StopCommand.Execute(null);

        Assert.Contains("RT:85", sent);
        Assert.False(vm.ContinuousActive);
        vm.StopContinuousJog();
        Assert.Equal(["$J=G91 X-200 F1000", "RT:85"], sent);
    }

    [Fact]
    public void Stop_when_not_held_sends_nothing()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());

        vm.StopContinuousJog();

        Assert.Empty(sent);
    }

    [Fact]
    public void Foam_together_hotkey_jogs_both_gantries_without_padlock()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));

        vm.JogTogether("X-,Y+|Z-,A+");

        Assert.Equal(["$J=G91 X-1 Y1 Z-1 A1 F1000"], sent);
    }

    [Fact]
    public void Foam_together_continuous_merges_both_gantries_without_padlock()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent,
            capabilities: ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] }), MakeSettings("Foam"));
        vm.IsDistance3 = true;

        vm.StartContinuousJog("X-,Y+|Z-,A+", allGantries: true);

        Assert.Equal(["$J=G91 X-200 Y300 Z-100 A50 F1000"], sent);
    }

    [Fact]
    public void All_gantries_flag_leaves_a_single_plane_tag_unchanged()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());

        vm.JogTogether("X+");

        Assert.Equal(["$J=G91 X1 F1000"], sent);
    }
}

public class JogLinearAxesTests
{
    [Fact]
    public void Mill_5axes_linear_rows_start_at_Z()
    {
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 5), JogViewModelTests.MakeSettings());
        Assert.Equal(["Z", "A", "B"], vm.LinearAxisLetters.Select(a => a.Letter));
    }

    [Fact]
    public void Foam_5axes_linear_rows_start_at_B()
    {
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 5), JogViewModelTests.MakeSettings("Foam"));
        var letters = vm.LinearAxisLetters.Select(a => a.Letter).ToList();
        Assert.Single(letters);
        Assert.Equal("B", letters[0]);
    }

    [Fact]
    public void Linear_rows_refresh_on_mode_switch()
    {
        var settings = JogViewModelTests.MakeSettings("Mill");
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 5), settings);
        Assert.Equal(3, vm.LinearAxisLetters.Count);

        var app = settings.Load();
        app.MachineTypeId = MachineTypes.Foam;
        settings.Save(app);

        Assert.Single(vm.LinearAxisLetters);
        Assert.Equal("B", vm.LinearAxisLetters[0].Letter);
    }
}

public class JogProxyInitialStateTests
{
    [Fact]
    public void Foam_axes_populate_immediately_through_proxy()
    {
        // Regression: through the proxy, the jog widget must see the machine's
        // axis count right away (foam 6 axes => crossed X/Y/Z/A + linear B/C),
        // not only after the first state emission.
        var proxy = new GrblServiceProxy(new JogViewModelTests.RecordingGrbl([], numAxes: 6));
        var vm = new JogViewModel(proxy, JogViewModelTests.MakeSettings("Foam"));

        Assert.Equal(["B", "C"], vm.LinearAxisLetters.Select(r => r.Letter));
    }
}

/// <summary>
/// Schema-driven jog layout (docs/machine-axis-schema.md M2): crosses derive
/// from the machine-type preset's planes, letters follow the controller's
/// [AXS:] report, and an under-axed multi-plane machine hides its second
/// plane (decision #3) instead of showing a dead cross.
/// </summary>
public class JogSchemaLayoutTests
{
    [Fact]
    public void Mill_has_no_second_plane_and_single_part_tags()
    {
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 4), JogViewModelTests.MakeSettings());
        Assert.False(vm.IsMultiPlane);
        Assert.False(vm.Plane2Visible);
        Assert.False(vm.Plane2HiddenHintVisible);
        // Plane-1 tags carry no coupled part; the conventional letters apply.
        Assert.Equal("X+", vm.RightTag);
        Assert.Equal("X", vm.P1H);
        Assert.Equal("Z", vm.P2H); // letter resolves, cross stays hidden
    }

    [Fact]
    public void Foam_on_four_axes_shows_second_plane_and_coupled_tags()
    {
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 4), JogViewModelTests.MakeSettings("Foam"));
        Assert.True(vm.IsMultiPlane);
        Assert.True(vm.Plane2Visible);
        Assert.False(vm.Plane2HiddenHintVisible);
        Assert.True(vm.TogetherVisible);
        Assert.Equal("X+|Z+", vm.RightTag);       // both horizontals
        Assert.Equal("Y+|A+", vm.UpTag);          // both verticals
        Assert.Equal("X-,Y+|Z-,A+", vm.UpLeftTag);
        Assert.Equal("Z+", vm.SecondRightTag);    // plane-2 cross is single-plane
        Assert.Equal("A", vm.P2V);
    }

    [Fact]
    public void Foam_on_three_axes_hides_second_plane_and_falls_back_to_linear_z()
    {
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 3), JogViewModelTests.MakeSettings("Foam"));
        Assert.True(vm.IsMultiPlane);
        Assert.False(vm.Plane2Visible);
        Assert.True(vm.Plane2HiddenHintVisible);
        Assert.False(vm.TogetherVisible);
        // The dead Z/A cross is gone; Z is still reachable as a linear row.
        Assert.Equal(["Z"], vm.LinearAxisLetters.Select(r => r.Letter));
        // Plane-1 tags carry no second part (Together cannot engage).
        Assert.Equal("X+", vm.RightTag);
    }

    [Fact]
    public void Controller_axis_letters_override_the_conventional_display()
    {
        var info = new CNC.Core.GrblInfoState { AxisLetters = "XYUV" }; // foam board flashed XYUV
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 4), JogViewModelTests.MakeSettings("Foam"), info);

        Assert.Equal("X", vm.P1H);
        Assert.Equal("U", vm.P2H);
        Assert.Equal("V", vm.P2V);
        Assert.Equal("X+|U+", vm.RightTag);
        Assert.Equal("Y+|V+", vm.UpTag);
        Assert.Equal("U+", vm.SecondRightTag);
    }

    [Fact]
    public void Together_tag_from_the_vm_jogs_both_planes()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl(sent), JogViewModelTests.MakeSettings("Foam"));
        vm.Together = true;

        vm.JogCommand.Execute(vm.UpLeftTag); // "X-,Y+|Z-,A+"

        Assert.Equal(["$J=G91 X-1 Y1 Z-1 A1 F1000"], sent);
    }

    [Fact]
    public void Letters_refresh_on_state_push_without_axis_count_change()
    {
        // A reconnect to a board with different letters but the same axis count
        // must still re-letter the crosses (ApplyState compares letters too). The
        // injected info instance is shared: mutating it simulates the new board.
        var info = new CNC.Core.GrblInfoState { AxisLetters = "XYZA" };
        var grbl = new JogViewModelTests.RecordingGrbl([], numAxes: 4);
        var vm = new JogViewModel(grbl, JogViewModelTests.MakeSettings("Foam"), info);
        Assert.Equal("A", vm.P2V);

        info.AxisLetters = "XYZB";
        grbl.Push(new MachineState(4)); // same count, different letters

        Assert.Equal("B", vm.P2V);
    }
}
