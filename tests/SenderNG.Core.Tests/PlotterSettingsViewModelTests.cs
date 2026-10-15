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

/// <summary>The plotter settings leaf after the world-mapping deletion: the four
/// view toggles, persistence, and the localization contract. The plane-row cases
/// (per-slot axis combos, origin offsets, permutation round trips) encode the
/// deleted model.</summary>
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

    /// <summary>The plane layout is the machine model's, not the section's: a
    /// foam profile and a mill profile produce the SAME editor surface — four
    /// toggles — because there is no per-plane mapping left to edit.</summary>
    [Fact]
    public void The_editor_is_the_same_for_every_machine_type()
    {
        var foam = Build(MachineTypes.Foam);
        Assert.True(foam.ShowGrid);
        Assert.True(foam.ShowRunState);
        Assert.Equal(ChromeFlags.Default, foam.Flags);
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
        // file also returns without the raise (:172). Do NOT invent a test-only
        // raiser on LocalizationService — that is a file this task must not touch.
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
            nameof(PlotterSettingsViewModel.ShowRunStateLabel), nameof(PlotterSettingsViewModel.ZoomInLabel),
            nameof(PlotterSettingsViewModel.ZoomOutLabel), nameof(PlotterSettingsViewModel.ZoomFitLabel),
            nameof(PlotterSettingsViewModel.SavePngLabel),
        })
            Assert.Contains(label, notified);
    }
}
