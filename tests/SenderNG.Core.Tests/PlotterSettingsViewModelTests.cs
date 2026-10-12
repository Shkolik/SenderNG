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

using System.Text;
using CNC.Core.Services;                       // SettingsService, MachineTypes
using SenderNG.Core.Localization;               // LocalizationService
using SenderNG.Modules.Plotter;                // ChromeFlags
using SenderNG.Modules.Plotter.Settings;

namespace SenderNG.Core.Tests;

public class PlotterSettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "plotter-settings-" + Guid.NewGuid().ToString("N"));

    public PlotterSettingsViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // Guarded: nothing guarantees the directory still exists, and an
        // unguarded delete throws and fails every test in the class.
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private PlotterSettingsViewModel Build(string machineTypeId = MachineTypes.Mill)
    {
        var settings = new SettingsService(_dir);
        var profile = settings.CreateProfile("p1", machineTypeId);
        settings.Activate(profile.Id);
        return new PlotterSettingsViewModel(settings, null);
    }

    [Fact]
    public void Every_toggle_starts_on()
    {
        var vm = Build();
        Assert.True(vm.ShowGrid);
        Assert.True(vm.ShowOrigin);
        Assert.True(vm.ShowEnvelope);
        Assert.True(vm.ShowRunState);
    }

    [Fact]
    public void Flipping_a_toggle_persists_through_the_settings_service()
    {
        var vm = Build();
        vm.ShowGrid = false;

        var reread = new PlotterSettingsViewModel(new SettingsService(_dir), null);
        Assert.False(reread.ShowGrid);
        Assert.True(reread.ShowOrigin);
    }

    [Fact]
    public void The_flags_mirror_the_four_toggles()
    {
        var vm = Build();
        Assert.Equal(ChromeFlags.Default, vm.Flags);
        vm.ShowEnvelope = false;
        Assert.False(vm.Flags.Envelope);
        Assert.True(vm.Flags.Grid);
    }

    [Fact]
    public void There_is_one_row_per_plane()
    {
        var vm = Build(MachineTypes.Foam);
        Assert.Equal(2, vm.PlaneRows.Count);
        Assert.Equal("Plane 1", vm.PlaneRows[0].PlaneLabel);
        Assert.Equal("Plane 2", vm.PlaneRows[1].PlaneLabel);
    }

    [Fact]
    public void A_mill_plane_offers_one_world_axis_choice_per_slot()
    {
        var vm = Build();
        Assert.Equal(3, vm.PlaneRows[0].Slots.Count);
        Assert.Equal(["X", "Y", "Z"], vm.PlaneRows[0].Slots[0].Choices);
        Assert.Equal([0, 1, 2], vm.PlaneRows[0].Slots.Select(s => s.Axis));
    }

    [Fact]
    public void A_foam_plane_offers_one_world_axis_choice_per_slot()
    {
        var vm = Build(MachineTypes.Foam);
        Assert.Equal(2, vm.PlaneRows[0].Slots.Count);
        Assert.Equal(["X", "Y", "Z"], vm.PlaneRows[0].Slots[0].Choices);
        Assert.Equal([0, 1], vm.PlaneRows[0].Slots.Select(s => s.Axis));
    }

    [Fact]
    public void Selecting_a_slot_axis_round_trips()
    {
        var vm = Build(MachineTypes.Foam);
        // Slot 0 takes Y; slot 1 keeps Z, its pick is distinct so nothing moves.
        vm.PlaneRows[1].Slots[0].Axis = 1;
        Assert.Equal([1, 0], vm.Model.Planes[1].WorldAxes);

        var reread = new PlotterSettingsViewModel(new SettingsService(_dir), null);
        Assert.Equal([1, 0], reread.Model.Planes[1].WorldAxes);
        Assert.Equal([1, 0], reread.PlaneRows[1].Slots.Select(s => s.Axis));
    }

    [Fact]
    public void A_stored_mapping_reads_into_the_slot_rows()
    {
        // A stored [1, 0] must SHOW as slot 0 = Y, slot 1 = X. The old combined
        // combo indexed permutations, and `List<int[]>.IndexOf` compared the
        // arrays by REFERENCE, so a saved non-identity mapping displayed as
        // identity in the UI; the per-slot rows read the stored indices directly.
        var settings = new SettingsService(_dir);
        var profile = settings.CreateProfile("p1", MachineTypes.Foam);
        PlotterConfig.Save(profile, new PlotterSettings
        {
            Planes = [new PlotterPlaneGeometry { WorldAxes = [0, 1] },
                      new PlotterPlaneGeometry { WorldAxes = [1, 0] }],
        });
        // The bag must reach the DISK before `Activate`: Activate reloads the
        // shared graph from the file (ProfilesStore.TryLoad), and SaveProfile
        // does not refresh it — so a bag written only in memory is invisible to
        // the view model's ctor `Load()` and the combo stays at index 0.
        settings.SaveProfile(profile);
        settings.Activate(profile.Id);

        var vm = new PlotterSettingsViewModel(settings, null);
        Assert.Equal([1, 0], vm.PlaneRows[1].Slots.Select(s => s.Axis));
    }

    [Fact]
    public void A_duplicate_slot_pick_nudges_the_other_slot_onto_the_free_axis()
    {
        // Slot 1 (currently Y) is given X — slot 0's axis. The user's pick
        // sticks and slot 0 is moved to the free axis (Y), mirroring the
        // editor's constraint that each carriage feeds a different world axis.
        var vm = Build(MachineTypes.Foam);
        vm.PlaneRows[0].Slots[1].Axis = 0;
        Assert.Equal([1, 0], vm.PlaneRows[0].Slots.Select(s => s.Axis));

        var reread = new PlotterSettingsViewModel(new SettingsService(_dir), null);
        Assert.Equal([1, 0], reread.Model.Planes[0].WorldAxes);
    }

    [Fact]
    public void An_origin_offset_round_trips()
    {
        var vm = Build();
        vm.PlaneRows[0].Origins[2].Value = 12.5;
        Assert.Equal(12.5, vm.Model.Planes[0].Origin[2]);

        var reread = new PlotterSettingsViewModel(new SettingsService(_dir), null);
        Assert.Equal(12.5, reread.Model.Planes[0].Origin[2]);
    }

    [Fact]
    public void A_malformed_stored_mapping_reads_back_as_identity()
    {
        var settings = new SettingsService(_dir);
        var profile = settings.CreateProfile("p1", MachineTypes.Mill);
        // Written straight into the profile's bag, because `PlotterConfig.Save`
        // calls `Normalize` and would coerce [7,7,7] to identity on the way IN —
        // the stored JSON would never contain it and `Load`'s rejection path
        // would never run. This bypasses Save to reach the loader. As with
        // `A_stored_permutation_is_selected_in_the_combo`, the bag must reach
        // the DISK before `Activate`, or the reloaded graph is section-less and
        // this test passes without ever entering the loader.
        profile.ModuleSettings[PlotterConfig.SectionKey] =
            """{"Planes":[{"WorldAxes":[7,7,7]}]}""";
        settings.SaveProfile(profile);
        settings.Activate(profile.Id);

        var vm = new PlotterSettingsViewModel(settings, null);
        Assert.Equal([0, 1, 2], vm.Model.Planes[0].WorldAxes);
    }

    [Fact]
    public void Labels_fall_back_to_english_without_a_localization_service()
    {
        var vm = Build();
        Assert.Equal("View", vm.ViewLabel);
        Assert.Equal("Show grid", vm.ShowGridLabel);
        Assert.Equal("Zoom in", vm.ZoomInLabel);
    }

    [Fact]
    public void Changed_fires_on_every_persisting_edit()
    {
        var vm = Build();
        var fired = 0;
        vm.Changed += () => fired++;
        vm.ShowGrid = false;
        vm.ShowOrigin = false;
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Relocalization_re_resolves_the_labels()
    {
        // LocalizationService's stream ctor parses a CATALOG, not a flat map:
        // the strings live under a "strings" object. A flat {"key":"value"} JSON
        // parses without throwing and silently yields an empty catalog.
        var catalog = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"strings":{"module.plotter.settings.view":"VIEW"}}"""));
        var loc = new LocalizationService(catalog);
        var vm = new PlotterSettingsViewModel(new SettingsService(_dir), loc);
        Assert.Equal("VIEW", vm.ViewLabel);
    }

    [Fact]
    public void A_locale_change_re_notifies_every_label()
    {
        // The original `RelocalizeLabels` listed ten of the eleven labels and
        // omitted ZoomFitLabel, so the "Fit" tooltip kept the old language while
        // every other label updated. Assert on the PropertyChanged NAMES, not on
        // resolved text, so a label that resolves identically still has to notify.
        //
        // `SetLocale` raises `Changed` ONLY for a directory-backed service: the
        // stream ctor leaves `_localeDirectory` null and SetLocale returns before
        // the raise (LocalizationService.cs:161): SetLocale() is a no-op for
        // stream-built services. And `ru.json` must exist too — a missing locale
        // file also
        // returns without the raise (:172). Do NOT invent a test-only raiser on
        // LocalizationService — that is a file this task must not touch.
        var locDir = Path.Combine(_dir, "loc");
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"),
            """{"strings":{"module.plotter.settings.view":"VIEW"}}""");
        File.WriteAllText(Path.Combine(locDir, "ru.json"),
            """{"strings":{"module.plotter.settings.view":"ВИД"}}""");
        var loc = LocalizationService.LoadFromDirectory(locDir, "en");
        var vm = new PlotterSettingsViewModel(new SettingsService(_dir), loc);

        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

        loc.SetLocale("ru");

        foreach (var label in new[]
        {
            nameof(PlotterSettingsViewModel.ViewLabel), nameof(PlotterSettingsViewModel.ShowGridLabel),
            nameof(PlotterSettingsViewModel.ShowOriginLabel), nameof(PlotterSettingsViewModel.ShowEnvelopeLabel),
            nameof(PlotterSettingsViewModel.ShowRunStateLabel), nameof(PlotterSettingsViewModel.WorldAxesLabel),
            nameof(PlotterSettingsViewModel.OriginLabel), nameof(PlotterSettingsViewModel.ZoomInLabel),
            nameof(PlotterSettingsViewModel.ZoomOutLabel), nameof(PlotterSettingsViewModel.ZoomFitLabel),
            nameof(PlotterSettingsViewModel.SavePngLabel),
        })
            Assert.Contains(label, notified);
    }
}