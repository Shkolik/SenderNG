/*
 * PlotterGlShowLoftTests.cs - the ShowLoft toggle: default, persistence,
 * vm binding, the HasLoft plane-count gate, and the runtime-push classifier
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

using System.Reflection;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.GL;
using SenderNG.Modules.Plotter.GL.Settings;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>Task 7's settings half: <c>ShowLoft</c> rides the module's
/// existing JSON section (default on), persists through the settings VM like
/// every other <c>Show*</c> toggle, is gated by a machine's plane count
/// (<see cref="PlotterGlSettingsViewModel.HasLoft"/> — a single-plane mill
/// has no sheet, so neither toolbar nor settings show the toggle), and a
/// ShowLoft-only profile save stays on the runtime-push path (no node
/// rebuild).</summary>
public class PlotterGlShowLoftTests
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "plotter-gl-showloft-" + Guid.NewGuid().ToString("N"));

    public PlotterGlShowLoftTests() => Directory.CreateDirectory(_dir);

    private static readonly MachineType Mill = MachineTypeCatalog.Resolve(MachineTypes.Mill);

    [Fact]
    public void ShowLoft_defaults_to_true_and_round_trips_through_the_profile()
    {
        var settings = new SettingsService(_dir);
        var profile = MakeMill(settings);

        var loaded = PlotterGlConfig.Load(profile, Mill);
        Assert.True(loaded.ShowLoft); // default: the sheet shows until hidden

        loaded.ShowLoft = false;
        PlotterGlConfig.Save(profile, loaded);
        settings.Save(profile);

        Assert.False(
            PlotterGlConfig.Load(settings.Load(), Mill).ShowLoft);
    }

    [Fact]
    public void ShowLoft_vm_property_saves_and_raises_PropertyChanged()
    {
        var settings = new SettingsService(_dir);
        MakeMill(settings);
        var vm = new PlotterGlSettingsViewModel(settings);

        Assert.True(vm.ShowLoft);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ShowLoft = false;

        Assert.Contains(nameof(PlotterGlSettingsViewModel.ShowLoft), changed);
        Assert.False(
            PlotterGlConfig.Load(settings.Load(), Mill).ShowLoft);
    }

    /// <summary>The plane-count gate: no sheet between a single plane and
    /// nothing, so the mill's view/settings carry no loft toggle while the
    /// two-plane foam does.</summary>
    [Fact]
    public void HasLoft_is_false_for_a_single_plane_machine_and_true_for_two()
    {
        var settings = new SettingsService(_dir);
        MakeMill(settings);
        Assert.False(new PlotterGlSettingsViewModel(settings).HasLoft);

        var foam = settings.CreateProfile("Foam", MachineTypes.Foam);
        settings.Activate(foam.Id);
        Assert.True(new PlotterGlSettingsViewModel(settings).HasLoft);
    }

    /// <summary>Structure-only classification: a save that differs in ShowLoft
    /// alone (same machine, same planes) stays on the runtime-push path, so
    /// flipping the sheet toggle never forces a node rebuild.</summary>
    [Fact]
    public void OnlyRuntimeStateChanged_returns_true_when_only_ShowLoft_differs()
    {
        var method = typeof(PlotterGlViewModel).GetMethod(
            "OnlyRuntimeStateChanged",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var a = new PlotterGlSettings { ShowLoft = true };
        var b = new PlotterGlSettings { ShowLoft = false };

        var result = (bool)method!.Invoke(null, [a, b, Mill, Mill])!;
        Assert.True(result);
    }

    private static MachineProfile MakeMill(SettingsService settings)
    {
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);
        return profile;
    }
}
