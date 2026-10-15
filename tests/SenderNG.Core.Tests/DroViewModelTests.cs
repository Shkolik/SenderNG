
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
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using SenderNG.Localization;
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

// Serialized with the other LocalizationState classes: DroKinematicsTests swaps
// LocalizationService.Current for its localized-warning test, and LocText reads that
// static from every DRO instance — including this class's, which runs in parallel
// otherwise and would see the Russian tip text mid-swap.
[Collection("LocalizationState")]
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

/// <summary>
/// The DRO against the resolved kinematics (invariant 10): every axis the controller
/// reports keeps a row — including one the profile never declared, which reads as an
/// unknown linear axis and is warned about exactly once — the rows are labelled with
/// the board's own letters, and a rotary axis reads in degrees instead of millimetres.
/// </summary>
[Collection("LocalizationState")] // the localized-warning test swaps LocalizationService.Current
public class DroKinematicsTests : IDisposable
{
    private readonly List<string> _dirs = new();

    /// <summary>Run <paramref name="body"/> with the real English catalog installed.
    /// <see cref="LocText"/> falls back to the caller's English literal when no service is
    /// set, so without this an assertion on the warning's sentence would depend on
    /// whether some other test happened to install a catalog first.</summary>
    private static void WithEnglishCatalog(Action body)
    {
        var previous = LocalizationService.Current;
        try
        {
            LocalizationService.Current = LocalizationService.LoadFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Locale"));
            body();
        }
        finally
        {
            LocalizationService.Current = previous;
        }
    }

    private static string UndeclaredAxesSentence() =>
        LocalizationService.Current!.T("appSettings.axes.problem.controller.undeclaredAxes");

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private SettingsService MakeSettings(bool withRotaryAxis)
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dro-kin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        var settings = new SettingsService(dir);
        var preset = MachineTypeCatalog.Resolve(MachineTypes.Mill);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        profile.Planes = preset.CreateDefaultPlanes();
        profile.OptionalAxes = [];
        if (withRotaryAxis)
        {
            profile.OptionalAxes.Add(new MachineAxis
            {
                Index = 3,
                Kind = AxisKind.Rotary,
                Direction = AxisDirection.Length,
                RotationOrigin = [0d, 0d],
            });
        }
        settings.Save(profile);
        return settings;
    }

    private sealed class CapabilityGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject;

        public CapabilityGrbl(BehaviorSubject<MachineState> subject, ControllerCapabilities capabilities)
        {
            _subject = subject;
            Capabilities = capabilities;
        }

        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public ControllerCapabilities Capabilities { get; set; }
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    private static ControllerCapabilities Board(int numAxes, string letters) =>
        new() { IsKnown = true, NumAxes = numAxes, AxisLetters = letters };

    [Fact]
    public void An_undeclared_reported_axis_still_renders_as_an_unknown_linear_axis()
    {
        // The profile declares three axes; the board reports four. The fourth row is
        // not hidden — it shows, labelled with the board's letter, as linear (mm).
        var settings = MakeSettings(withRotaryAxis: false);
        var subject = new BehaviorSubject<MachineState>(
            new MachineState { NumAxes = 4, Positions = [1, 2, 3, 4] });
        var grbl = new CapabilityGrbl(subject, Board(4, "XYZA"));
        using var kinematics = new MachineKinematicsProvider(settings, grbl);

        WithEnglishCatalog(() =>
        {
            var vm = new DroViewModel(grbl, kinematics);

            Assert.Equal(4, vm.Axes.Count);
            Assert.Equal(["X", "Y", "Z", "A"], vm.Axes.Select(a => a.Letter));
            Assert.False(vm.Axes[3].IsRotary);
            Assert.Equal(string.Empty, vm.Axes[3].Unit);
            Assert.Equal(4, vm.Axes[3].Position);
            // The warning is the catalog's sentence for the problem's Code: the DRO
            // localizes, and KinematicsProblem.Message is developer-facing English only.
            Assert.Equal(UndeclaredAxesSentence(), vm.Status);
        });
    }

    [Fact]
    public void A_rotary_axis_reads_in_degrees()
    {
        var settings = MakeSettings(withRotaryAxis: true); // optional rotary at index 3
        var subject = new BehaviorSubject<MachineState>(
            new MachineState { NumAxes = 4, Positions = [1, 2, 3, 45] });
        var grbl = new CapabilityGrbl(subject, Board(4, "XYZA"));
        using var kinematics = new MachineKinematicsProvider(settings, grbl);

        var vm = new DroViewModel(grbl, kinematics);

        Assert.Equal(4, vm.Axes.Count);
        Assert.True(vm.Axes[3].IsRotary);
        Assert.Equal("°", vm.Axes[3].Unit);
        Assert.False(vm.Axes[0].IsRotary);
        Assert.Equal(string.Empty, vm.Axes[0].Unit);
        Assert.Equal(string.Empty, vm.Status); // declared on both sides: nothing to warn about
    }

    [Fact]
    public void Rows_are_labelled_with_the_boards_letters()
    {
        // A remapped board: index 2 is U and index 3 is V, so the DRO's zero button
        // addresses the axis the board actually answers to.
        var settings = MakeSettings(withRotaryAxis: false);
        var subject = new BehaviorSubject<MachineState>(
            new MachineState { NumAxes = 4, Positions = [1, 2, 3, 4] });
        var grbl = new CapabilityGrbl(subject, Board(4, "XYUV"));
        using var kinematics = new MachineKinematicsProvider(settings, grbl);

        var vm = new DroViewModel(grbl, kinematics);

        Assert.Equal(["X", "Y", "U", "V"], vm.Axes.Select(a => a.Letter));
    }

    [Fact]
    public void The_undeclared_axes_warning_is_the_catalogs_sentence_not_the_resolvers_english()
    {
        // The resolver's Message is English developer text (KinematicsProblem.Message);
        // the DRO is a localized UI, so its status must be the catalog sentence for the
        // problem's Code — the same mapping the machine-card editor renders. Run in a
        // real non-English locale so an English string reaching the status is visible.
        var settings = MakeSettings(withRotaryAxis: false);
        var subject = new BehaviorSubject<MachineState>(
            new MachineState { NumAxes = 4, Positions = [1, 2, 3, 4] });
        var grbl = new CapabilityGrbl(subject, Board(4, "XYZA"));
        using var kinematics = new MachineKinematicsProvider(settings, grbl);

        var previous = LocalizationService.Current;
        try
        {
            LocalizationService.Current = LocalizationService.LoadFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Locale"), "ru");

            var vm = new DroViewModel(grbl, kinematics);

            Assert.Equal(
                LocalizationService.Current.T("appSettings.axes.problem.controller.undeclaredAxes"),
                vm.Status);
            Assert.DoesNotContain("not declared", vm.Status);
        }
        finally
        {
            LocalizationService.Current = previous;
        }
    }

    [Fact]
    public void The_undeclared_axes_warning_fires_once_across_repeated_updates()
    {
        var settings = MakeSettings(withRotaryAxis: false);
        var subject = new BehaviorSubject<MachineState>(
            new MachineState { NumAxes = 3, Positions = [1, 2, 3] });
        var grbl = new CapabilityGrbl(subject, Board(3, "XYZ"));
        using var kinematics = new MachineKinematicsProvider(settings, grbl);

        WithEnglishCatalog(() =>
        {
            var vm = new DroViewModel(grbl, kinematics);
            Assert.Equal(string.Empty, vm.Status); // three declared, three reported

            var expected = UndeclaredAxesSentence();
            var warnings = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DroViewModel.Status) && vm.Status == expected)
                    warnings++;
            };

            // The board reveals a fourth axis: one warning, then no repeat however many
            // status reports this continuously-refreshing view consumes.
            grbl.Capabilities = Board(4, "XYZA");
            kinematics.Refresh();
            subject.OnNext(new MachineState { NumAxes = 4, Positions = [1, 2, 3, 4] });
            subject.OnNext(new MachineState { NumAxes = 4, Positions = [1, 2, 3, 5] });
            subject.OnNext(new MachineState { NumAxes = 4, Positions = [1, 2, 3, 6] });

            Assert.Equal(1, warnings);
            Assert.Equal(4, vm.Axes.Count);
        });
    }
}
