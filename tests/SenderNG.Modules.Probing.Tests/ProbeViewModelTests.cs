
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

using System.Reflection;
using Avalonia.Headless.XUnit;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Plugins;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Probing.Core;
using Xunit;

namespace SenderNG.Modules.Probing.Tests;

[Collection("LocalizationState")] // serializes with other tests mutating LocalizationService.Current
public class ProbeViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly SettingsService _settings;

    public ProbeViewModelTests()
    {
        // AttachServices now captures a process-wide dispatcher; without this
        // reset the tests that follow it would queue onto a dispatcher nobody
        // pumps, making the suite order-dependent. See the same hazard recorded
        // for PlotterUi in tests/SenderNG.Tests/PlotterTestStubs.cs:125-139.
        UiThread.ResetForTests();
        _dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(_dir);
    }

    private async Task<DemoGrblService> ConnectDemoAsync()
    {
        var demo = new DemoGrblService(_settings.Load());
        await demo.ConnectAsync("demo");
        return demo;
    }

    [AvaloniaFact]
    public void AttachServices_initializes_the_marshaller()
    {
        var module = new ProbingModule();
        var sp = new ServiceCollection()
            .AddSingleton(_settings)
            .BuildServiceProvider();
        try
        {
            Assert.False(UiThread.IsInitialized);
            module.AttachServices(sp);
            Assert.True(UiThread.IsInitialized);
        }
        finally
        {
            UiThread.ResetForTests();
        }
    }

    [Fact]
    public async Task ToolLength_start_probes_Z_and_reports_offset()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        await vm.StartAsync();

        Assert.True(vm.LastResult.Contains("G43.1Z-11.5"), $"Unexpected result: {vm.LastResult}");
    }

    [Fact]
    public async Task Switching_tools_clears_the_last_result()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        await vm.StartAsync(); // default tool: ToolLength
        Assert.NotEmpty(vm.LastResult);

        // The result is tool-local: another tab must not show the TLO message.
        vm.SelectedTool = ProbingType.HeightMap;
        Assert.Equal(string.Empty, vm.LastResult);

        // The edge-finder internal/external flip (same tab, radio switch) is
        // a tool change too and clears as well.
        vm.SelectedTool = ProbingType.EdgeFinderInternal;
        Assert.Equal(string.Empty, vm.LastResult);
    }

    [Fact]
    public async Task Tool_table_availability_reflects_controller_capabilities()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        // Demo is grblHAL Full with 4 tool slots -> tool table present.
        Assert.True(vm.HasToolTable);
        Assert.Equal([0, 1, 2, 3, 4], vm.ToolChoices.Select(t => t.Id));
        Assert.False(vm.ProbeToolTable); // 0 selected -> dynamic offset

        vm.SelectedToolNumber = 2;
        Assert.True(vm.ProbeToolTable);
    }

    [Fact]
    public async Task Tool_selector_lists_none_then_slots_with_names_and_defaults_to_none()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        // Items: None first, then T1..T4 (no names yet -> bare Tx labels).
        Assert.Equal(
            ["None", "T1", "T2", "T3", "T4"],
            vm.ToolChoices.Select(t => t.DisplayName));
        Assert.Equal(0, vm.SelectedToolNumber); // None selected by default
        Assert.Equal("None", vm.SelectedToolChoice?.DisplayName);

        // Save a name for T2 under the demo controller key (as the Offsets
        // editor would on "Set all") and the label picks it up.
        var app = _settings.Load();
        var config = OffsetsConfigStore.Load(app);
        config.ToolNamesFor("demo")[2] = "Ø3 spot drill";
        OffsetsConfigStore.Save(app, config);
        _settings.Save(app);

        Assert.Equal(
            ["None", "T1", "T2 (Ø3 spot drill)", "T3", "T4"],
            vm.ToolChoices.Select(t => t.DisplayName));
    }

    [Fact]
    public async Task Tool_selector_follows_the_visible_tools_setting()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        // Demo has 4 tool slots; default VisibleTools (8) clamps to 4.
        Assert.Equal([0, 1, 2, 3, 4], vm.ToolChoices.Select(t => t.Id));

        // App Settings -> Offsets: "Visible tools" 8 -> 2 propagates here (the
        // picker must NOT keep listing all firmware slots).
        var app = _settings.Load();
        app.VisibleTools = 2;
        _settings.Save(app);

        Assert.Equal([0, 1, 2], vm.ToolChoices.Select(t => t.Id));
        Assert.Equal(0, vm.SelectedToolNumber); // None still selected

        // A selection beyond the reduced count clamps back into range.
        vm.SelectedToolNumber = 2;
        Assert.Equal(2, vm.SelectedToolNumber);
        vm.SelectedToolNumber = 5;
        Assert.Equal(2, vm.SelectedToolNumber);
        Assert.True(vm.ProbeToolTable);

        // 0 hides every slot — only "None" remains selectable.
        app = _settings.Load();
        app.VisibleTools = 0;
        _settings.Save(app);
        Assert.Equal([0], vm.ToolChoices.Select(t => t.Id));
        vm.SelectedToolNumber = 1;
        Assert.Equal(0, vm.SelectedToolNumber); // clamped to None-only
        Assert.False(vm.ProbeToolTable);
    }

    [Fact]
    public async Task ToolLength_with_tool_stores_to_tool_table_and_applies_G43()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedToolNumber = 2;

        await vm.StartAsync();

        Assert.True(
            vm.LastResult.StartsWith("Tool length offset stored to T2:", StringComparison.Ordinal),
            $"Unexpected result: {vm.LastResult}");
        Assert.Contains("G10L1P2Z-11.5", vm.LastResult);
        // The demo stores the probed Z into the tool-table slot; verify the probe
        // actually persisted it (ioSender only scaffolded this path).
        var stored = Assert.Single(demo.WorkParameters.Tools, t => t.Id == 2);
        Assert.Equal(-11.5, stored.Offsets[2], 3);
    }

    [Fact]
    public async Task ToolLength_without_tool_keeps_dynamic_G43_and_never_writes_table()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        // Default SelectedToolNumber = 0 -> dynamic G43.1, no G10 L1.
        vm.SelectedToolNumber = 0;

        await vm.StartAsync();

        Assert.Contains("G43.1Z-11.5", vm.LastResult);
        Assert.DoesNotContain("G10L1", vm.LastResult);
    }

    [Fact]
    public async Task ToolLength_with_add_offset_writes_G10()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.AddAction = true;
        vm.WorkpieceHeight = 20;
        vm.TouchPlateHeight = 1;

        await vm.StartAsync();

        Assert.True(vm.LastResult.Contains("G10L2P1Z21"), $"Unexpected result: {vm.LastResult}");
    }

    [Fact]
    public async Task Profile_add_persists_and_loads_in_new_instance()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.ProbeFeedRate = 250;
        vm.NewProfileName = "fast";

        vm.ProfileAddCommand.Execute(null);

        var second = new ProbeViewModel(demo, _settings);
        Assert.Equal(2, second.Profiles.Count);
        var fast = second.Profiles.Single(p => p.Name == "fast");
        Assert.Equal(250, fast.ProbeFeedRate);
    }

    [Fact]
    public async Task Selected_profile_loads_its_values_into_working_fields()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.ProbeDiameter = 6;
        vm.ProbeDistance = 25;
        vm.NewProfileName = "big";
        vm.ProfileAddCommand.Execute(null);

        var second = new ProbeViewModel(demo, _settings);
        second.SelectedProfile = second.Profiles.Single(p => p.Name == "big");

        Assert.Equal(6, second.ProbeDiameter);
        Assert.Equal(25, second.ProbeDistance);
    }

    [Fact]
    public async Task SelectedProfile_ignores_null_keeps_previous_while_typing_new_name()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        var original = vm.SelectedProfile!;

        // The editable combo nulls the selection while a typed name does not
        // match a profile; the VM must keep the previous profile so Update/Add
        // still know what is being edited.
        vm.SelectedProfile = null;
        vm.NewProfileName = "renamed";

        Assert.Same(original, vm.SelectedProfile);
        Assert.Equal("renamed", vm.NewProfileName);
    }

    [Fact]
    public void Profiles_file_derives_from_the_profile_id()
    {
        // The legacy ProfilesFile override key is gone after the settings split:
        // the companion path derives from the active profile id.
        var settings = _settings.Load();
        var expectedPath = ProfileSettingsPaths.CompanionPath(_settings.SettingsDir, settings.Id, ProbingProfileStore.CompanionSuffix);

        var demo = new DemoGrblService(settings);
        var vm = new ProbeViewModel(demo, _settings);
        vm.ProbeFeedRate = 175;
        vm.NewProfileName = "custom";
        vm.ProfileAddCommand.Execute(null);

        Assert.True(File.Exists(expectedPath));
        Assert.Equal(2, new ProbeViewModel(demo, _settings).Profiles.Count);
    }

    [Fact]
    public void ProbeViewModel_repoints_its_companion_store_after_switch()
    {
        var vm = new ProbeViewModel(new StubGrbl(), _settings);
        vm.NewProfileName = "p";
        vm.ProfileAddCommand.Execute(null);
        Assert.True(File.Exists(ProfileSettingsPaths.CompanionPath(_dir, "default", ProbingProfileStore.CompanionSuffix)));

        var second = _settings.CreateProfile("second", MachineTypes.Mill);
        _settings.Activate(second.Id);

        Assert.Single(vm.Profiles); // the new machine's companion (default profile only)
        vm.NewProfileName = "q";
        vm.ProfileAddCommand.Execute(null);
        Assert.True(File.Exists(ProfileSettingsPaths.CompanionPath(_dir, "second", ProbingProfileStore.CompanionSuffix)));
        Assert.DoesNotContain("p", vm.Profiles.Select(p => p.Name));
    }

    [Fact]
    public async Task ToolLength_with_tool_requests_work_parameter_refresh()
    {
        var demo = await ConnectDemoAsync();
        var recording = new RecordingGrblService(demo);
        var vm = new ProbeViewModel(recording, _settings);
        vm.SelectedToolNumber = 2;

        Assert.Equal(0, recording.RefreshCalls);
        await vm.StartAsync();

        // After G10 L1 Pn + G43 Hn the probe re-reads $# so the Offsets tool
        // table / Work Parameters widgets update immediately on a real board
        // (the demo is already live, so the publish happens via the G10 L1 path).
        Assert.Equal(1, recording.RefreshCalls);
    }

    // --------------------------------------------------- apply: height map + rotation

    [Fact]
    public async Task Height_map_apply_runs_the_real_probe_then_transforms_the_program()
    {
        var demo = await ConnectDemoAsync();
        var program = new TransformTargetStub();
        var services = new ServiceCollection()
            .AddSingleton<IGrblService>(demo)
            .AddSingleton<IProgramTransformTarget>(program)
            .BuildServiceProvider();
        var vm = new ProbeViewModel(demo, _settings, services);
        program.LoadGCode("G1 X10 Y0 F100");

        vm.SelectedTool = ProbingType.HeightMap;
        await vm.StartAsync();

        Assert.True(vm.HasHeightMap, $"Height map probe failed: {vm.LastResult}");
        vm.HeightMapApplyCommand.Execute(null);

        Assert.Equal("Height map applied to program", vm.LastResult);
        // The demo surface is flat -> corrections are 0, but the program is
        // rebuilt at the token level: split at the grid spacing, Z re-stated,
        // feed hoisted ahead of the segments.
        Assert.Equal(
            [
                "(Height map applied)",
                "F100",
                "G1X5Y0Z0",
                "G1X10Y0Z0",
            ],
            program.LoadedText!.Split('\n'));
    }

    [Fact]
    public async Task Height_map_apply_requires_a_map_first()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.HeightMapApplyCommand.Execute(null);

        Assert.Equal("Probe or load a height map first", vm.LastResult);
    }

    [Fact]
    public async Task New_probe_session_clears_the_stale_map_and_un_gates_the_preview()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedTool = ProbingType.HeightMap;
        await vm.StartAsync();
        Assert.True(vm.HasHeightMap, $"Height map probe failed: {vm.LastResult}");

        // A new session invalidates the previous map: the preview gate must
        // drop at run start (the synchronous prefix of StartAsync), not stay
        // enabled while the snapshot is already cleared for renderers.
        vm.SelectedTool = ProbingType.ToolLength;
        var run = vm.StartAsync();
        Assert.False(vm.HasHeightMap);

        await run;
        Assert.False(vm.HasHeightMap);
    }

    [Fact]
    public async Task Returning_to_the_height_map_tab_re_publishes_the_loaded_map()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedTool = ProbingType.HeightMap;
        await vm.StartAsync();
        Assert.True(vm.HasHeightMap, $"Height map probe failed: {vm.LastResult}");

        var seen = new List<HeightMapLayer?>();
        vm.HeightMapChanged += layer => seen.Add(layer);

        vm.SelectedTool = ProbingType.CenterFinder; // away: snapshot cleared
        vm.SelectedTool = ProbingType.HeightMap;    // back: map still loaded

        Assert.Equal(2, seen.Count);
        Assert.Null(seen[0]);
        Assert.NotNull(seen[1]);
    }

    [Fact]
    public async Task Rotation_apply_rotates_about_the_selected_origin()
    {
        var demo = await ConnectDemoAsync();
        var program = new TransformTargetStub();
        var services = new ServiceCollection()
            .AddSingleton<IGrblService>(demo)
            .AddSingleton<IProgramTransformTarget>(program)
            .BuildServiceProvider();
        var vm = new ProbeViewModel(demo, _settings, services);
        program.LoadGCode("G1 X50 Y0 F100\nG1 X100 Y0 F100");

        // The angle is normally written by RunRotationAsync after a live probe;
        // drive the chain from a measured 15° edge (the Core invariant tests
        // cover the probe-math side of the chain).
        typeof(ProbeViewModel).GetField("_probedAngleRadians",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, 15d * Math.PI / 180d);
        vm.RotationOrigin = RotationOrigin.C; // program-limits corner (max X, max Y) = (100, 0)

        vm.RotationApplyCommand.Execute(null);

        Assert.Equal("Program rotated", vm.LastResult);
        // Pivot (100, 0) stays fixed; the (0,0) -> (50,0) move's endpoint
        // swings to (51.704, -12.941) — the corner resolution is exercised.
        Assert.Contains("G1X100Y0", program.LoadedText);
        Assert.Contains("G1X51.704Y-12.941", program.LoadedText);
    }

    [Fact]
    public async Task Rotation_apply_is_gated_on_a_probed_angle()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.RotationApplyCommand.Execute(null);

        Assert.Equal("Probe an edge angle first", vm.LastResult);
    }

    [Fact]
    public async Task Rotation_origin_selector_lists_all_pivots_defaulting_to_zero()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        Assert.Equal(11, vm.RotationOriginNames.Count);
        Assert.Equal(RotationOrigin.None, vm.RotationOrigin);
        Assert.Equal(0, vm.RotationOriginIndex);
    }

    [Fact]
    public void Rotation_origin_names_are_positional_and_follow_live_locale_change()
    {
        // Labels are positional (no A/B/C/D letters): the probing diagram's
        // letter convention differs from ioSender's, and AB is the BOTTOM edge
        // here — ioSender's "Edge AB (top)" was actively wrong for us.
        var locDir = Path.Combine(Path.GetTempPath(), "senderng-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"),
            """{"strings": {"module.probing.origin.a": "Bottom-left corner", "module.probing.origin.ab": "Bottom edge", "module.probing.origin.cd": "Top edge"}}""");
        File.WriteAllText(Path.Combine(locDir, "ru.json"),
            """{"strings": {"module.probing.origin.a": "Левый нижний угол", "module.probing.origin.ab": "Нижняя кромка", "module.probing.origin.cd": "Верхняя кромка"}}""");
        var svc = LocalizationService.LoadFromDirectory(locDir);
        var previous = LocalizationService.Current;
        LocalizationService.Current = svc;
        try
        {
            var vm = new ProbeViewModel(new StubGrbl(), _settings);

            Assert.Equal("Bottom-left corner", vm.RotationOriginNames[1]);
            Assert.Equal("Bottom edge", vm.RotationOriginNames[6]);
            Assert.Equal("Top edge", vm.RotationOriginNames[9]);

            // Live locale switch: the selector must follow without a restart
            // (the VM re-resolves the names on LocalizationService.Changed;
            // SetLocale is always raised on the UI thread in the app, so the
            // update is synchronous).
            svc.SetLocale("ru");

            Assert.Equal("Левый нижний угол", vm.RotationOriginNames[1]);
            Assert.Equal("Нижняя кромка", vm.RotationOriginNames[6]);
            Assert.Equal("Верхняя кромка", vm.RotationOriginNames[9]);
        }
        finally
        {
            LocalizationService.Current = previous;
            Directory.Delete(locDir, true);
        }
    }

    [Fact]
    public async Task Rotation_determine_x0y0_probes_the_left_edge_and_sets_work_zero()
    {
        // End-to-end through the demo: AB probes at (0,4)/(5,4) plus the left
        // edge probe at (4,5); radius 1 compensation puts the corner at (5,5),
        // which is written into G54 (G10 L2 P1) after riding to it in machine
        // coordinates. LatchDistance = 0 so the demo records the fast-probe
        // targets (the latch pass would offset them by latch + slow travel).
        var demo = await ConnectDemoAsync();
        var capturing = new RecordingGrblService(demo);
        var vm = new ProbeViewModel(capturing, _settings);
        vm.SelectedTool = ProbingType.Rotation;
        vm.LatchDistance = 0;
        vm.ProbeEdge = Edge.AB;
        vm.AddAction = true;

        await vm.StartAsync();

        Assert.Contains("G53G0X5Y5", capturing.Lines);
        Assert.Contains("G10L2P1X5Y5", capturing.Lines);
        Assert.Equal("X0,Y0 set at the probed corner: G10L2P1X5Y5", vm.LastResult);
    }

    [Fact]
    public async Task Rotation_determine_x0y0_with_g92_zeroes_the_corner_position()
    {
        var demo = await ConnectDemoAsync();
        var capturing = new RecordingGrblService(demo);
        var vm = new ProbeViewModel(capturing, _settings);
        vm.SelectedTool = ProbingType.Rotation;
        vm.LatchDistance = 0;
        vm.IsCoordG92 = true;
        vm.ProbeEdge = Edge.AB;
        vm.AddAction = true;

        await vm.StartAsync();

        Assert.Contains("G53G0X5Y5", capturing.Lines);
        Assert.Contains("G92X0Y0", capturing.Lines);
        Assert.Equal("X0,Y0 set at the probed corner: G92X0Y0", vm.LastResult);
    }

    [Fact]
    public async Task Rotation_determine_x0y0_in_measure_mode_reports_the_corner()
    {
        var demo = await ConnectDemoAsync();
        var capturing = new RecordingGrblService(demo);
        var vm = new ProbeViewModel(capturing, _settings);
        vm.SelectedTool = ProbingType.Rotation;
        vm.LatchDistance = 0;
        vm.IsCoordMeasure = true;
        vm.ProbeEdge = Edge.AB;
        vm.AddAction = true;

        await vm.StartAsync();

        // The corner is still ridden to, but nothing is written to the controller.
        Assert.Contains("G53G0X5Y5", capturing.Lines);
        Assert.DoesNotContain(capturing.Lines, l => l.StartsWith("G92") || l.StartsWith("G10"));
        Assert.Equal("Probed corner: X5 Y5", vm.LastResult);
    }

    [Fact]
    public async Task Rotation_determine_x0y0_forces_ab_and_unchecks_on_other_edges()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedTool = ProbingType.Rotation;

        // Checking the extra forces the AB edge and the (0,0) pivot.
        vm.ProbeEdge = Edge.AD;
        vm.AddAction = true;
        Assert.Equal(Edge.AB, vm.ProbeEdge);
        Assert.Equal(RotationOrigin.None, vm.RotationOrigin);
        Assert.False(vm.RotationOriginEnabled);

        // Picking another edge drops the extra and re-enables the pivot combo.
        vm.ProbeEdge = Edge.CB;
        Assert.False(vm.AddAction);
        Assert.True(vm.RotationOriginEnabled);
    }

    // ------------------------------------------------------------- preview

    [Fact]
    public async Task Preview_show_generates_the_current_tool_program_and_updates_live()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedTool = ProbingType.ToolLength;
        vm.ProbeFixture = false;

        // Always on: the box is seeded at construction, no checkbox involved.
        Assert.Equal(
            ["G91F100", "G38.3F100Z-10", "!G0Z0.5", "G38.3F25Z-2"],
            vm.PreviewText.Split('\n'));

        // Live regeneration: tweaking an input rebuilds the program.
        vm.ProbeDistance = 15;
        Assert.Contains("G38.3F100Z-15", vm.PreviewText.Split('\n'));
    }

    [Fact]
    public async Task Preview_switching_tools_regenerates_the_program()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedTool = ProbingType.Rotation;
        vm.ProbeEdge = Edge.AB;

        Assert.Contains("G38.3F100Y10", vm.PreviewText.Split('\n'));

        vm.SelectedTool = ProbingType.HeightMap;
        Assert.Contains("#Probing point 1 of", vm.PreviewText);
        Assert.Contains("G38.3F100Z-10", vm.PreviewText.Split('\n'));
    }

    [Fact]
    public async Task Preview_rotation_with_determine_x0y0_shows_the_left_edge_probe()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedTool = ProbingType.Rotation;
        vm.ProbeEdge = Edge.AB;
        vm.AddAction = true;
        vm.LatchDistance = 0; // one G38 per probe action

        // Two Y probes on the bottom edge plus the X probe on the left edge.
        var lines = vm.PreviewText.Split('\n');
        Assert.Equal(3, lines.Count(l => l.Contains("G38", StringComparison.Ordinal)));
        Assert.Contains("G38.3F100X10", lines);
    }

    [Fact]
    public async Task Preview_edge_finder_shows_the_z_probe_phase_when_enabled()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedTool = ProbingType.EdgeFinderExternal;
        vm.ProbeEdge = Edge.A;
        vm.ProbeZ = true;

        // Both phases, separated like ioSender's preview.
        var lines = vm.PreviewText.Split('\n');
        Assert.Contains("; Post XY probe", lines);
        Assert.Contains("G38.3F100Z-10", lines);
    }

    [Fact]
    public void Edge_finder_Z_cell_forces_ProbeZ_and_restores_it_on_leaving()
    {
        var vm = new ProbeViewModel(new StubGrbl(), _settings);
        Assert.False(vm.ProbeZ);
        Assert.True(vm.IsProbeZEditable);

        // Selecting the Z cell implies the top-surface probe: auto-checked.
        vm.ProbeEdge = Edge.Z;
        Assert.True(vm.ProbeZ);
        Assert.False(vm.IsProbeZEditable); // checkbox locked while Z is selected

        // Leaving Z restores the pre-Z state (ProbeZ was auto-forced, not user-chosen).
        vm.ProbeEdge = Edge.AD;
        Assert.False(vm.ProbeZ);
        Assert.True(vm.IsProbeZEditable);

        // A manual choice survives the Z trip (ioSender behavior).
        vm.ProbeZ = true;
        vm.ProbeEdge = Edge.Z;
        vm.ProbeEdge = Edge.CD;
        Assert.True(vm.ProbeZ);
        Assert.True(vm.IsProbeZEditable);

        // Internal probing has no Z cell (a center probe inside a pocket would
        // touch the pocket bottom, not the top surface): entering internal
        // mode while Z is selected moves to an edge and restores the
        // auto-forced Probe Z.
        vm.ProbeZ = false;
        vm.ProbeEdge = Edge.Z;
        Assert.True(vm.ProbeZ);
        vm.SelectedTool = ProbingType.EdgeFinderInternal;
        Assert.Equal(Edge.AD, vm.ProbeEdge);
        Assert.False(vm.ProbeZ);
        Assert.True(vm.IsProbeZEditable);

        // A manually chosen Probe Z survives the internal switch (not forced).
        vm.ProbeZ = true;
        vm.ProbeEdge = Edge.Z;
        vm.SelectedTool = ProbingType.EdgeFinderInternal;
        Assert.Equal(Edge.AD, vm.ProbeEdge);
        Assert.True(vm.ProbeZ);
        Assert.True(vm.IsProbeZEditable);
    }

    [AvaloniaFact]
    public void Enabled_tools_roundtrip_through_module_settings()
    {
        // Seed the profile with the height map disabled, then save + reload
        // from disk. Plain STJ deserialization turns the nested module section
        // into JsonElement, which used to defeat the Dictionary<string, object>
        // reads and made every tool appear enabled again (regression: unchecking
        // a tool in App Settings did not persist).
        var profile = _settings.Load();
        profile.ModuleSettings["Probing"] = new Dictionary<string, object>
        {
            [ProbingModule.EnabledToolsKey] = new Dictionary<string, object>
            {
                ["tool-length"] = true,
                ["edge-finder"] = true,
                ["rotation"] = true,
                ["center"] = true,
                ["heightmap"] = false,
            },
        };
        _settings.Save(profile);

        var onDisk = ProfilesStore.TryLoad(_dir, MachineProfile.DefaultId)!;
        Assert.IsNotType<Dictionary<string, object>>(onDisk.ModuleSettings["Probing"]);

        // A fresh SettingsService re-reads the file (restart-equivalent); the
        // view model must see the height map as disabled and hide its tab.
        var freshSettings = new SettingsService(_dir);
        var vm = new ProbeViewModel(new StubGrbl(), freshSettings);
        Assert.False(vm.HeightMapEnabled);
        Assert.True(vm.ToolLengthEnabled);
        Assert.True(vm.EdgeFinderEnabled);

        // The App Settings checkbox reads the persisted state after a reopen...
        var module = new ProbingModule();
        module.AttachServices(new ServiceCollection().AddSingleton(freshSettings).BuildServiceProvider());
        var fields = module.GetBoolSettingFields().ToDictionary(f => f.Key!);
        Assert.False(fields["module.probing.heightmap"].Get());
        Assert.True(fields["module.probing.tool-length"].Get());

        // ...and toggling through the field setter persists across a fresh read.
        fields["module.probing.heightmap"].Set(true);
        var module2 = new ProbingModule();
        module2.AttachServices(new ServiceCollection().AddSingleton(new SettingsService(_dir)).BuildServiceProvider());
        Assert.True(module2.GetBoolSettingFields().Single(f => f.Key == "module.probing.heightmap").Get());
        fields["module.probing.heightmap"].Set(false);
        var module3 = new ProbingModule();
        module3.AttachServices(new ServiceCollection().AddSingleton(new SettingsService(_dir)).BuildServiceProvider());
        Assert.False(module3.GetBoolSettingFields().Single(f => f.Key == "module.probing.heightmap").Get());
    }

    [AvaloniaFact]
    public void Enabled_tools_toggle_roundtrips_in_memory_without_restart()
    {
        // The App Settings flow in one session: uncheck tools through the
        // module field setter (mutates the shared graph + saves), then reopen
        // the dialog (fresh module on the SAME SettingsService). Regression:
        // the setter stored a Dictionary<string, bool> which the generic `is`
        // checks in ReadEnabledTools reject (generic invariance), so the
        // in-memory re-read defaulted every tool back to enabled — the dialog
        // showed ALL tools checked until the app was restarted (only the disk
        // re-read produced a tolerated JsonElement shape).
        var module = new ProbingModule();
        module.AttachServices(new ServiceCollection().AddSingleton(_settings).BuildServiceProvider());
        var fields = module.GetBoolSettingFields().ToDictionary(f => f.Key!);
        fields["module.probing.heightmap"].Set(false);
        fields["module.probing.center"].Set(false);

        var reopened = new ProbingModule();
        reopened.AttachServices(new ServiceCollection().AddSingleton(_settings).BuildServiceProvider());
        var reopenedFields = reopened.GetBoolSettingFields().ToDictionary(f => f.Key!);
        Assert.False(reopenedFields["module.probing.heightmap"].Get());
        Assert.False(reopenedFields["module.probing.center"].Get());
        Assert.True(reopenedFields["module.probing.tool-length"].Get());

        // Toggling back re-enables both, still without touching disk re-reads.
        reopenedFields["module.probing.heightmap"].Set(true);
        reopenedFields["module.probing.center"].Set(true);
        var reopened2 = new ProbingModule();
        reopened2.AttachServices(new ServiceCollection().AddSingleton(_settings).BuildServiceProvider());
        var fields2 = reopened2.GetBoolSettingFields().ToDictionary(f => f.Key!);
        Assert.True(fields2["module.probing.heightmap"].Get());
        Assert.True(fields2["module.probing.center"].Get());
    }

    [AvaloniaFact]
    public void Enabled_tools_toggle_applies_to_the_open_view_model()
    {
        // The probe panel is open when the user edits App Settings: the toggle
        // must reach the live VM through SettingsService.Changed (same profile
        // instance — the profile-switch branch cannot catch this). Regression:
        // enabled tools were re-read only on profile switches, so the change
        // required an app restart to show up.
        var vm = new ProbeViewModel(new StubGrbl(), _settings);
        Assert.True(vm.CenterEnabled);
        Assert.True(vm.ToolTabs[3].IsEnabled);

        var module = new ProbingModule();
        module.AttachServices(new ServiceCollection().AddSingleton(_settings).BuildServiceProvider());
        var fields = module.GetBoolSettingFields().ToDictionary(f => f.Key!);

        fields["module.probing.center"].Set(false);
        Assert.False(vm.CenterEnabled);
        Assert.False(vm.ToolTabs[3].IsEnabled);
        Assert.True(vm.ToolTabs[0].IsEnabled);

        // Re-enabling restores the tab in place (panels are never recreated).
        fields["module.probing.center"].Set(true);
        Assert.True(vm.CenterEnabled);
        Assert.True(vm.ToolTabs[3].IsEnabled);
    }

    [Fact]
    public void Center_single_axis_modes_preview_only_that_axis()
    {
        var vm = new ProbeViewModel(new StubGrbl(), _settings);
        vm.SelectedTool = ProbingType.CenterFinder;
        vm.WorkpieceSizeX = 40;
        vm.WorkpieceSizeY = 30;

        vm.ProbeCenter = Center.XInside;
        Assert.Contains("G38.3F100X-10", vm.PreviewText);
        Assert.DoesNotContain("G38.3F100Y", vm.PreviewText);

        vm.ProbeCenter = Center.YOutside;
        Assert.Contains("G38.3F100Y10", vm.PreviewText);
        Assert.DoesNotContain("G38.3F100X", vm.PreviewText);
    }

    [Fact]
    public void Center_design_instance_defaults_to_X_inside()
    {
        var vm = ProbeViewModel.CreateDesignInstance(ProbingType.CenterFinder);
        Assert.Equal(Center.XInside, vm.ProbeCenter);
    }

    // ------------------------------------------ offset send: success + failure

    [Fact]
    public async Task Edge_finder_run_writes_the_offset_and_reports_completion()
    {
        var demo = await ConnectDemoAsync();
        var recording = new RecordingGrblService(demo);
        var vm = new ProbeViewModel(recording, _settings);
        vm.SelectedTool = ProbingType.EdgeFinderExternal;
        vm.ProbeEdge = Edge.A;

        await vm.StartAsync();

        Assert.StartsWith("Probing completed: G10L2", vm.LastResult);
        Assert.Contains(recording.Lines, l => l.StartsWith("G10L2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Edge_finder_offset_send_failure_reports_failure_not_completion()
    {
        var demo = await ConnectDemoAsync();
        var failing = new FaultingOffsetGrblService(demo);
        var vm = new ProbeViewModel(failing, _settings);
        vm.SelectedTool = ProbingType.EdgeFinderExternal;
        vm.ProbeEdge = Edge.A;

        await vm.StartAsync();

        Assert.Contains(failing.Lines, l => l.StartsWith("G10L2", StringComparison.Ordinal));
        Assert.Contains("transport write failed", vm.LastResult);
        Assert.DoesNotContain("Probing completed", vm.LastResult);
    }

    [Fact]
    public async Task Center_finder_run_writes_the_offset_and_reports_completion()
    {
        var demo = await ConnectDemoAsync();
        var recording = new RecordingGrblService(demo);
        var vm = new ProbeViewModel(recording, _settings);
        vm.SelectedTool = ProbingType.CenterFinder;
        vm.ProbeCenter = Center.XInside;

        await vm.StartAsync();

        Assert.StartsWith("Probing completed: ", vm.LastResult);
        Assert.Contains("G10L2", vm.LastResult);
        Assert.Contains(recording.Lines, l => l.StartsWith("G10L2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Center_finder_offset_send_failure_reports_failure_not_completion()
    {
        var demo = await ConnectDemoAsync();
        var failing = new FaultingOffsetGrblService(demo);
        var vm = new ProbeViewModel(failing, _settings);
        vm.SelectedTool = ProbingType.CenterFinder;
        vm.ProbeCenter = Center.XInside;

        await vm.StartAsync();

        Assert.Contains(failing.Lines, l => l.StartsWith("G10L2", StringComparison.Ordinal));
        Assert.Contains("transport write failed", vm.LastResult);
        Assert.DoesNotContain("Probing completed", vm.LastResult);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>App-side program surface the apply commands rewrite — the plugin
    /// only ever talks to <see cref="IProgramTransformTarget"/>, never the app
    /// view model, so the plugin tests drive the real height-map/rotation apply
    /// flow through this stand-in (mirrors ProgramViewModel.LoadGCode + limits).</summary>
    private sealed class TransformTargetStub : IProgramTransformTarget
    {
        private GCodeProgram? _program;

        public string? LoadedText { get; private set; }
        public IReadOnlyList<AxisLimits> Limits { get; private set; } = [];

        public void LoadGCode(string? text)
        {
            var program = GCodeProgram.Parse(text);
            LoadProgram(program, text);
        }

        public void LoadProgram(GCodeProgram program, string? text)
        {
            _program = program;
            LoadedText = text;
            Limits = GCodeLimitsCalculator.Compute(program.Tokens) ?? [];
        }
    }

    /// <summary>Minimal non-connecting IGrblService for VM construction tests.</summary>
    private sealed class StubGrbl : CNC.Core.Abstractions.IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public bool IsConnected => false;
        public System.IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    /// <summary>Wraps an IGrblService, counting RefreshWorkParametersAsync calls
    /// and recording every sent line.</summary>
    private sealed class RecordingGrblService : CNC.Core.Abstractions.IGrblService
    {
        private readonly IGrblService _inner;
        public int RefreshCalls { get; private set; }
        public List<string> Lines { get; } = new();
        public RecordingGrblService(IGrblService inner) => _inner = inner;
        public bool IsConnected => _inner.IsConnected;
        public event EventHandler? ConnectionChanged { add => _inner.ConnectionChanged += value; remove => _inner.ConnectionChanged -= value; }
        public ControllerCapabilities Capabilities => _inner.Capabilities;
        public WorkParameters WorkParameters => _inner.WorkParameters;
        public IObservable<MachineState> State => _inner.State;
        public IObservable<WorkParameters> WorkParametersChanges => _inner.WorkParametersChanges;
        public Task ConnectAsync(string port) => _inner.ConnectAsync(port);
        public void Disconnect() => _inner.Disconnect();
        public void SendRealtime(byte code) => _inner.SendRealtime(code);
        public Task SendAsync(string line) { Lines.Add(line); return _inner.SendAsync(line); }
        public Task RefreshWorkParametersAsync() { RefreshCalls++; return _inner.RefreshWorkParametersAsync(); }
    }

    /// <summary>Wraps an IGrblService, recording every sent line and failing the
    /// G10 offset write with a faulted task (the send surfaces its exception to
    /// the awaiter instead of throwing synchronously).</summary>
    private sealed class FaultingOffsetGrblService : CNC.Core.Abstractions.IGrblService
    {
        private readonly IGrblService _inner;
        public List<string> Lines { get; } = new();
        public FaultingOffsetGrblService(IGrblService inner) => _inner = inner;
        public bool IsConnected => _inner.IsConnected;
        public event EventHandler? ConnectionChanged { add => _inner.ConnectionChanged += value; remove => _inner.ConnectionChanged -= value; }
        public ControllerCapabilities Capabilities => _inner.Capabilities;
        public WorkParameters WorkParameters => _inner.WorkParameters;
        public IObservable<MachineState> State => _inner.State;
        public IObservable<WorkParameters> WorkParametersChanges => _inner.WorkParametersChanges;
        public Task ConnectAsync(string port) => _inner.ConnectAsync(port);
        public void Disconnect() => _inner.Disconnect();
        public void SendRealtime(byte code) => _inner.SendRealtime(code);

        public Task SendAsync(string line)
        {
            Lines.Add(line);
            return line.StartsWith("G10", StringComparison.Ordinal)
                ? Task.FromException(new IOException("transport write failed"))
                : _inner.SendAsync(line);
        }

        public Task RefreshWorkParametersAsync() => _inner.RefreshWorkParametersAsync();
    }
}
