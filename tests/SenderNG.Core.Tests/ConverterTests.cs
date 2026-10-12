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

extern alias hpgl;

using SenderNG.Modules.Excellon;
using SenderNG.Modules.Shared;
// HPGL-specific types live in the aliased SenderNG.Modules.Hpgl assembly (the
// linked shared scaffolding is compiled into both plugin DLLs, so shared types
// resolve from the Excellon assembly only).
using HpglModule = hpgl::SenderNG.Modules.Hpgl.HpglModule;
using HpglToGCode = hpgl::SenderNG.Modules.Hpgl.HpglToGCode;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

public class ConverterTests
{
    // ---- Module contract ----

    [Fact]
    public void Modules_expose_filter_identity_and_locale_keys()
    {
        var excellon = new ExcellonModule();
        var hpgl = new HpglModule();

        Assert.Equal("excellon", excellon.Id);
        Assert.Equal("drl,xln", excellon.FileExtensions);
        Assert.Equal("module.excellon.fileType", excellon.FileTypeKey);
        Assert.False(excellon.ShowCutMode);
        Assert.Contains("module.excellon.name", excellon.LocalizationKeys.Keys);

        Assert.Equal("hpgl", hpgl.Id);
        Assert.Equal("plt", hpgl.FileExtensions);
        Assert.Equal("module.hpgl.fileType", hpgl.FileTypeKey);
        Assert.True(hpgl.ShowCutMode); // cut/laser selector shown for HPGL only
        Assert.Contains("module.hpgl.name", hpgl.LocalizationKeys.Keys);
    }

    [Fact]
    public void Modules_carry_optional_plugin_metadata()
    {
        Assert.True(new ExcellonModule() is IPluginInfo);
        Assert.True(new HpglModule() is IPluginInfo);

        foreach (var info in new IPluginInfo[] { new ExcellonModule(), new HpglModule() })
        {
            Assert.False(string.IsNullOrWhiteSpace(info.Version));
            Assert.False(string.IsNullOrWhiteSpace(info.Author));
            Assert.False(string.IsNullOrWhiteSpace(info.License));
        }
    }

    // ---- Excellon ----

    private static ConverterParameters Defaults() => new();

    [Fact]
    public void Excellon_metric_decimal_drills_emit_g81_chain()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X1.5Y1.5\n" +
            "X2.0Y2.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        Assert.Contains("(Translated by Excellon to GCode converter)", output);
        Assert.Contains("G90G17G21G50", output);
        Assert.Contains("G0Z1", output);
        Assert.Contains("M6 (MSG, 2.4 mm drill)", output); // tool below configured 3 mm → drill
        Assert.Contains("M3S5000", output);
        Assert.Contains("G4P1", output);
        Assert.Contains("G81X1.5Y1.5Z-1.8R1", output);
        Assert.Contains("G81X2Y2R1", output); // Z unchanged since the first drill
        Assert.EndsWith("M30", output.TrimEnd());
    }

    [Fact]
    public void Excellon_oversized_tool_emits_helical_g2_instead_of_g81()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C5\n" +
            "%\n" +
            "T01\n" +
            "X1.5Y1.5\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // r = (5 - 3) / 2 = 1 → start at X + 1, full circle via G2 I-1
        Assert.Contains("G0X2.5Y1.5", output);
        Assert.Contains("G1Z-1.8", output);
        Assert.Contains("G2X2.5I-1", output);
        Assert.DoesNotContain("G81", output);
        Assert.Contains("M6 (MSG, 3 mm mill)", output); // capped at configured diameter
    }

    [Fact]
    public void Excellon_slot_g85_expands_to_g81_chain()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X2.0Y2.0G85X3.0Y2.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        Assert.Contains("(Slot 2;2 - 3;2)", output);
        Assert.Contains("G81X2Y2Z-1.8R1", output);
        Assert.Contains("G81X3R1", output); // only changed words are emitted
    }

    // ---- Excellon: slot interpolation bound ----

    [Fact]
    public void Excellon_slot_positions_for_a_zero_diameter_tool_stays_small()
    {
        // A T..C0.0 header entry makes the old expression Infinity, whose (int)
        // cast is int.MinValue, and the caller's loop then runs ~2^31 times.
        Assert.InRange(Excellon2GCode.SlotPositions(1.0, 0d), 2, 10000);
    }

    [Fact]
    public void Excellon_slot_positions_for_a_negative_diameter_tool_stays_small()
    {
        Assert.InRange(Excellon2GCode.SlotPositions(1.0, -2.4d), 2, 10000);
    }

    [Fact]
    public void Excellon_slot_positions_are_clamped_for_an_absurdly_small_tool()
    {
        // A positive but tiny tool would overflow the (int) cast the same way.
        Assert.InRange(Excellon2GCode.SlotPositions(1.0, 1e-300d), 2, 10000);
    }

    [Fact]
    public void Excellon_slot_positions_are_unchanged_for_a_normal_slot()
    {
        // 1 mm of travel with a 2.4 mm tool: 1 / 0.8 = 1.25 → 1, floored to 2.
        Assert.Equal(2, Excellon2GCode.SlotPositions(1.0, 2.4d));
        // 10 mm of travel with a 2.4 mm tool: 10 / 0.8 = 12.5 → 13.
        Assert.Equal(13, Excellon2GCode.SlotPositions(10.0, 2.4d));
    }

    [Fact]
    public void Excellon_zero_diameter_tool_does_not_hang_the_converter()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C0.0\n" +
            "%\n" +
            "T01\n" +
            "X2.0Y2.0G85X3.0Y2.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // The slot is still emitted (clamping shortens the pitch, it does not
        // truncate the slot) and the position count is bounded.
        Assert.Contains("(Slot 2;2 - 3;2)", output);
        Assert.InRange(CountOccurrences(output, "G81"), 1, 10000);
    }

    // ---- Excellon: modal coordinates ----

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            n++;
            at += needle.Length;
        }
        return n;
    }

    [Fact]
    public void Excellon_x_only_line_carries_over_the_previous_y()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X1.5Y1.5\n" +
            "X2.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // The second line has no Y, so the hole lands at the previous Y. The g-code
        // emitter is modal too, so the unchanged Y is simply omitted — a broken
        // carry-over would instead emit "G81X2Y0R1" and fail this.
        Assert.Contains("G81X1.5Y1.5Z-1.8R1", output);
        Assert.Contains("G81X2R1", output);
    }

    [Fact]
    public void Excellon_y_only_line_carries_over_the_previous_x()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X1.5Y1.5\n" +
            "Y2.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // A Y-only line used to be a silent no-op and lost the hole entirely. A
        // dropped carry-over would emit "G81X0Y2R1" and fail this.
        Assert.Contains("G81X1.5Y1.5Z-1.8R1", output);
        Assert.Contains("G81Y2R1", output);
    }

    [Fact]
    public void Excellon_modal_run_of_x_only_lines_keeps_every_hole()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X1.0Y1.0\n" +
            "X2.0\n" +
            "X3.0\n" +
            "Y4.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // Before the fix this produced exactly one G81: the X-only and Y-only
        // lines were dropped, so three holes vanished from the program.
        Assert.Contains("G81X1Y1Z-1.8R1", output);
        Assert.Contains("G81X2R1", output);   // Y carried over from (1,1)
        Assert.Contains("G81X3R1", output);   // Y still carried over
        Assert.Contains("G81Y4R1", output);   // Y-only line, X carried over
        Assert.Equal(4, CountOccurrences(output, "G81"));
    }

    [Fact]
    public void Excellon_slot_end_inherits_the_start_axis_it_omits()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X2.0Y2.0G85X3.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // G85 with no end Y means the slot runs along X at the start's Y.
        Assert.Contains("(Slot 2;2 - 3;2)", output);
    }

    [Fact]
    public void Excellon_unparseable_line_is_reported_in_the_output()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X1.5Y1.5\n" +
            "XbadYbad\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // Silent loss is the bug; the bad line must now say so.
        Assert.Contains("skipped", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Excellon_clean_file_reports_no_skipped_lines()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X1.5Y1.5\n" +
            "X2.0\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        // Output parity for clean files must not change.
        Assert.DoesNotContain("skipped", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Excellon_inch_converts_to_mm()
    {
        const string drl =
            "M48\n" +
            "INCH\n" +
            "T01C0.1\n" +
            "%\n" +
            "T01\n" +
            "X0.1Y0.2\n" +
            "M95\n";

        var output = new Excellon2GCode().Convert(drl, Defaults());

        Assert.NotNull(output);
        Assert.Contains("G81X0.004Y0.008Z-1.8R1", output); // 0.1in / 25.4 = 0.00394 → 0.004
    }

    [Fact]
    public void Excellon_scale_and_custom_parameters_flow_into_output()
    {
        const string drl =
            "M48\n" +
            "METRIC\n" +
            "T01C2.4\n" +
            "%\n" +
            "T01\n" +
            "X1.5Y1.5\n" +
            "M95\n";

        var p = new ConverterParameters { ZHome = 30, ZRapids = 5, ZSafe = 2, ZMin = -3, RPM = 8000, FeedRate = 400, PlungeRate = 150, ScaleX = 2, ScaleY = 2 };
        var output = new Excellon2GCode().Convert(drl, p);

        Assert.NotNull(output);
        Assert.Contains("G51X2Y2", output);
        Assert.Contains("G0Z5", output);
        Assert.Contains("G0X0Y0Z30", output);
        Assert.Contains("M3S8000", output);
        Assert.Contains("G81X1.5Y1.5Z-3R2", output);
    }

    // ---- HPGL ----

    /// <summary>Defaults from the HPGL assembly: the linked shared scaffolding is
    /// compiled into each plugin DLL, so the parameters type passed to
    /// HpglToGCode must come from the same assembly as the converter.</summary>
    private static hpgl::SenderNG.Modules.Shared.ConverterParameters HpglDefaults() => new();

    /// <summary>25 mm square (1000 plotter units × 0.025).</summary>
    private const string SquarePlt =
        "IN;\n" +
        "PU;PA0,0;\n" +
        "PD;PA1000,0;PA1000,1000;PA0,1000;PA0,0;PU;\n";

    [Fact]
    public void Hpgl_square_cut_offsets_polygon_outward_by_half_tool()
    {
        var p = HpglDefaults();
        p.SourceName = "board.Edge_Cuts.plt";
        var output = new HpglToGCode().Convert(SquarePlt, p);

        Assert.NotNull(output);
        Assert.Contains("(Translated by HPGL to GCode converter)", output);
        Assert.Contains("(Tool diameter: 3 mm)", output);
        Assert.Contains("G90G91.1G17G21G50", output);
        Assert.Contains("M3S5000", output); // cut preamble
        Assert.Contains("(Polygon: 1, vertices: 4, clockwise: False)", output);
        Assert.Contains("G0X-1.5Y-1.5Z1", output); // outward by tool radius
        Assert.Contains("G1X26.5F300", output); // only changed words are emitted
        Assert.Contains("G1Y26.5", output);
        Assert.Contains("G1X-1.5", output);
        Assert.Contains("G1Y-1.5", output);
        Assert.EndsWith("M30", output.TrimEnd());
    }

    [Fact]
    public void Hpgl_square_without_tool_diameter_keeps_original_contour()
    {
        var p = HpglDefaults();
        p.SourceName = "board.plt";
        p.ToolDiameter = 0;
        var output = new HpglToGCode().Convert(SquarePlt, p);

        Assert.NotNull(output);
        Assert.Contains("G1Z-1.8F100", output); // plunge at (0,0) — no X/Y words
        Assert.Contains("G1X25F300", output); // only changed words are emitted
        Assert.Contains("G1Y25", output);
        Assert.Contains("G1X0", output);
        Assert.Contains("G1Y0", output);
    }

    [Fact]
    public void Hpgl_paste_plt_emits_laser_preamble_and_insets_polygon()
    {
        var p = HpglDefaults();
        p.SourceName = "solder.Paste.plt";
        var output = new HpglToGCode().Convert(SquarePlt, p);

        Assert.NotNull(output);
        Assert.Contains("M122P1", output);
        Assert.Contains("M123P800", output);
        Assert.Contains("M124P1500", output);
        Assert.Contains("M125Q1P2", output);
        Assert.Contains("M4S90", output);
        Assert.Contains("M7", output);
        Assert.Contains("M8", output);
        Assert.DoesNotContain("M3S", output);
        Assert.Contains("G0X1.5Y1.5Z1", output); // inset by tool radius
    }

    [Fact]
    public void Hpgl_arc_edge_emits_g3_with_ij()
    {
        // Degenerate arc (center == start): the only AA contour that chains in
        // ioSender's converter — after an AA the pen position becomes the arc
        // CENTER, so real arc contours break polygon chaining upstream too.
        // Faithful parity test for the G3 emission path.
        const string plt =
            "IN;PU;PA0,0;PD;PA1000,0;AA1000,0,9000;PA0,0;PU;\n";

        var p = HpglDefaults();
        p.SourceName = "board.Edge_Cuts.plt";
        p.ToolDiameter = 0;
        var output = new HpglToGCode().Convert(plt, p);

        Assert.NotNull(output);
        Assert.Contains("(Polygon: 1, vertices: 3, clockwise: False)", output);
        Assert.Contains("G3I0J0", output); // degenerate arc — end equals pen position
    }

    [Fact]
    public void Hpgl_each_polygon_in_a_file_gets_its_own_id()
    {
        // The id is pre-incremented inside the while-loop condition, so every
        // iteration advances it. Hoisting that increment out of the loop would
        // hand both polygons id 1 and fail this assertion.
        const string plt =
            "IN;\n" +
            "PU;PA0,0;\n" +
            "PD;PA1000,0;PA1000,1000;PA0,1000;PA0,0;PU;\n" +
            "PU;PA5000,0;\n" +
            "PD;PA6000,0;PA6000,1000;PA5000,1000;PA5000,0;PU;\n";

        var p = HpglDefaults();
        p.SourceName = "board.Edge_Cuts.plt";
        p.ToolDiameter = 0;
        var output = new HpglToGCode().Convert(plt, p);

        Assert.NotNull(output);
        Assert.Contains("(Polygon: 1,", output);
        Assert.Contains("(Polygon: 2,", output);
    }

    [Fact]
    public void Hpgl_polygon_ids_restart_per_conversion_on_a_shared_instance()
    {
        // ids are instance state reset at the top of Convert, so reusing one
        // converter restarts at 1 instead of continuing to climb. Dropping the
        // `_polygonId = 0;` statement fails this on the second call.
        var converter = new HpglToGCode();
        var p = HpglDefaults();
        p.SourceName = "board.Edge_Cuts.plt";
        p.ToolDiameter = 0;

        var first = converter.Convert(SquarePlt, p);
        var second = converter.Convert(SquarePlt, p);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Contains("(Polygon: 1,", first);
        Assert.Contains("(Polygon: 1,", second);
    }

    // ---- Parameters dialog VM ----

    [Fact]
    public void ConverterParametersViewModel_round_trips_values()
    {
        var vm = new ConverterParametersViewModel(new ConverterSettings { ZHome = 12, FeedRate = 333, ScaleY = 0.5 });
        Assert.Equal(12, vm.ZHome);
        Assert.Equal(333, vm.FeedRate);

        vm.ZHome = 99;
        vm.ScaleY = 2;
        var s = vm.ToSettings();
        Assert.Equal(99, s.ZHome);
        Assert.Equal(333, s.FeedRate);
        Assert.Equal(2, s.ScaleY);
        Assert.True(s.HasValues); // accepted values count as stored
    }

    [Fact]
    public void ConverterParametersViewModel_defaults_when_nothing_stored()
    {
        var vm = new ConverterParametersViewModel(new ConverterSettings());
        Assert.Equal(25, vm.ZHome);
        Assert.Equal(5000, vm.RPM);
        Assert.Equal(1, vm.ScaleX);
    }

    [Fact]
    public void DoNotShowAgain_maps_to_show_dialog_flag()
    {
        var stored = new ConverterSettings { ShowDialog = false, HasValues = true };
        var vm = new ConverterParametersViewModel(stored);
        Assert.True(vm.DoNotShowAgain); // prefilled from the stored flag

        vm.DoNotShowAgain = false;
        Assert.True(vm.ToSettings().ShowDialog);

        vm.DoNotShowAgain = true;
        Assert.False(vm.ToSettings().ShowDialog);
    }

    // ---- ConverterParameters / store ----

    [Fact]
    public void ConverterParameters_clone_is_independent()
    {
        var a = new ConverterParameters { ZHome = 40, FeedRate = 500 };
        var b = a.Clone();
        b.ZHome = 10;
        Assert.Equal(40, a.ZHome);
        Assert.Equal(500, b.FeedRate);
    }

    [Fact]
    public void ConverterSettingsStore_round_trips_per_module()
    {
        var profile = new MachineProfile();
        var saved = new ConverterSettings
        {
            ShowDialog = false,
            ZHome = 42,
            RPM = 7777,
            CutMode = false,
            HasValues = true,
        };
        ConverterSettingsStore.Save(profile, "hpgl", saved);

        var loaded = ConverterSettingsStore.Load(profile, "hpgl");
        Assert.Equal(42, loaded.ZHome);
        Assert.Equal(7777, loaded.RPM);
        Assert.False(loaded.CutMode);
        Assert.False(loaded.ShowDialog);
        Assert.True(loaded.HasValues);

        // Unrelated modules keep their own defaults.
        Assert.Equal(25, ConverterSettingsStore.Load(profile, "excellon").ZHome);
        Assert.True(ConverterSettingsStore.Load(profile, "excellon").ShowDialog);
    }

    [Fact]
    public void ConverterSettingsStore_corrupt_entry_falls_back_to_defaults()
    {
        var profile = new MachineProfile();
        profile.ModuleSettings["excellon"] = "{ not json";
        var loaded = ConverterSettingsStore.Load(profile, "excellon");
        Assert.Equal(25, loaded.ZHome);
        Assert.True(loaded.ShowDialog);
    }

    // ---- Module open flow (ConvertAsync) ----

    private sealed class StubDialogService : IDialogService
    {
        public object? LastVm { get; private set; }
        public bool? Result { get; set; } = true;

        public Task<bool?> ShowDialogAsync(string title, object viewModel, (double Width, double Height)? preferredSize = null)
        {
            LastVm = viewModel;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubViewFactory : IViewFactory
    {
        public void Register<TViewModel, TView>() where TView : class { }
        public object Create(object viewModel) => new object();
    }

    private static ServiceProvider MakeFlowProvider(SettingsService settings, IDialogService dialogs) =>
        new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton<IDialogService>(dialogs)
            .AddSingleton<IViewFactory>(new StubViewFactory())
            .BuildServiceProvider();

    [Fact]
    public async Task Module_convert_asks_parameters_then_converts_and_persists()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N")));
        var dialogs = new StubDialogService();
        var provider = MakeFlowProvider(settings, dialogs);

        const string drl = "M48\nMETRIC\nT01C2.4\n%\nT01\nX1.5Y1.5\nM95\n";
        var output = await new ExcellonModule().ConvertAsync(provider, "board.drl", drl);

        Assert.NotNull(output);
        Assert.Contains("G81X1.5Y1.5Z-1.8R1", output);
        Assert.IsType<ConverterParametersViewModel>(dialogs.LastVm); // dialog asked
        Assert.True(ConverterSettingsStore.Load(settings.Load(), "excellon").HasValues);
    }

    [Fact]
    public async Task Module_convert_with_showDialog_off_converts_silently()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N")));
        var profile = settings.Load();
        var stored = new ConverterSettings { ShowDialog = false, ZHome = 9, HasValues = true };
        ConverterSettingsStore.Save(profile, "excellon", stored);
        settings.Save(profile);

        var dialogs = new StubDialogService();
        var provider = MakeFlowProvider(settings, dialogs);

        const string drl = "M48\nMETRIC\nT01C2.4\n%\nT01\nX1.5Y1.5\nM95\n";
        var output = await new ExcellonModule().ConvertAsync(provider, "board.drl", drl);

        Assert.Null(dialogs.LastVm); // never asked
        Assert.NotNull(output);
        Assert.Contains("G81X1.5Y1.5Z-1.8R1", output); // stored ZMin used
    }

    [Fact]
    public async Task Module_convert_cancel_returns_null_and_persists_nothing()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N")));
        var dialogs = new StubDialogService { Result = false };
        var provider = MakeFlowProvider(settings, dialogs);

        const string drl = "M48\nMETRIC\nT01C2.4\n%\nT01\nX1.5Y1.5\nM95\n";
        var output = await new ExcellonModule().ConvertAsync(provider, "board.drl", drl);

        Assert.Null(output);
        Assert.False(ConverterSettingsStore.Load(settings.Load(), "excellon").HasValues);
    }

    [Fact]
    public async Task Module_convert_shows_dialog_when_nothing_stored_even_if_flag_off()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N")));
        var profile = settings.Load();
        ConverterSettingsStore.Save(profile, "excellon", new ConverterSettings { ShowDialog = false });
        settings.Save(profile);

        var dialogs = new StubDialogService();
        var provider = MakeFlowProvider(settings, dialogs);

        const string drl = "M48\nMETRIC\nT01C2.4\n%\nT01\nX1.5Y1.5\nM95\n";
        await new ExcellonModule().ConvertAsync(provider, "board.drl", drl);

        // No parameters ever accepted → asking beats converting with invented defaults.
        Assert.IsType<ConverterParametersViewModel>(dialogs.LastVm);
    }
}