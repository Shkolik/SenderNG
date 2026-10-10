/*
 * PlotterGlMsaaTests.cs - the MSAA mode setting: default, persistence,
 * index binding, and normalization of a value outside the enum
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

using System.Text.Json.Nodes;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.GL.Settings;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>Step 5b.4's settings half: <c>MsaaMode</c> rides the module's
/// existing JSON section (default None until proven), the settings leaf binds
/// it through an int ComboBox index, and a value outside the enum — which
/// System.Text.Json happily produces from hand-edited or forward-dated
/// profile JSON — is normalized back to <see cref="MsaaMode.None"/> instead
/// of reaching the renderer.</summary>
public class PlotterGlMsaaTests
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "plotter-gl-msaa-" + Guid.NewGuid().ToString("N"));

    public PlotterGlMsaaTests() => Directory.CreateDirectory(_dir);

    private static readonly MachineType Mill = MachineTypeCatalog.Resolve(MachineTypes.Mill);

    [Fact]
    public void Msaa_mode_defaults_to_none_and_round_trips_through_the_profile()
    {
        var settings = new SettingsService(_dir);
        var profile = MakeMill(settings);

        var loaded = PlotterGlConfig.Load(profile, Mill);
        Assert.Equal(MsaaMode.None, loaded.MsaaMode); // default: proven first

        loaded.MsaaMode = MsaaMode.X4;
        PlotterGlConfig.Save(profile, loaded);
        settings.Save(profile);

        Assert.Equal(
            MsaaMode.X4,
            PlotterGlConfig.Load(settings.Load(), Mill).MsaaMode);
    }

    /// <summary>System.Text.Json deserializes ANY integer into an enum (no
    /// validation), so a profile carrying 9 must not reach
    /// <see cref="GlReadback"/>-facing code as a bogus mode.</summary>
    [Fact]
    public void An_out_of_range_msaa_value_normalizes_to_none()
    {
        var settings = new SettingsService(_dir);
        var profile = MakeMill(settings);

        var s = PlotterGlConfig.Load(profile, Mill);
        s.MsaaMode = MsaaMode.X4;
        PlotterGlConfig.Save(profile, s);

        var section = JsonNode.Parse(
            (string)profile.ModuleSettings[PlotterGlConfig.SectionKey])!.AsObject();
        section["MsaaMode"] = 9;
        profile.ModuleSettings[PlotterGlConfig.SectionKey] = section.ToJsonString();

        Assert.Equal(MsaaMode.None, PlotterGlConfig.Load(profile, Mill).MsaaMode);

        // ...and the clamp tracks the enum as it grows: with X8 defined, 4 is
        // the first value past the members — still a forward-dated profile
        // value, still normalized back to the plain path.
        section["MsaaMode"] = 4;
        profile.ModuleSettings[PlotterGlConfig.SectionKey] = section.ToJsonString();

        Assert.Equal(MsaaMode.None, PlotterGlConfig.Load(profile, Mill).MsaaMode);
    }

    /// <summary>Checkpoint-5 item 4: 8x must survive the module's JSON section
    /// like every other member — save, reload, still 8x.</summary>
    [Fact]
    public void Msaa_8x_round_trips_through_the_profile()
    {
        var settings = new SettingsService(_dir);
        var profile = MakeMill(settings);

        var loaded = PlotterGlConfig.Load(profile, Mill);
        loaded.MsaaMode = MsaaMode.X8;
        PlotterGlConfig.Save(profile, loaded);
        settings.Save(profile);

        Assert.Equal(
            MsaaMode.X8,
            PlotterGlConfig.Load(settings.Load(), Mill).MsaaMode);
    }

    [Fact]
    public void Msaa_index_binds_the_combo_box_and_persists_through_the_vm()
    {
        var settings = new SettingsService(_dir);
        MakeMill(settings);
        var vm = new PlotterGlSettingsViewModel(settings);

        Assert.Equal(MsaaMode.None, vm.MsaaMode);
        Assert.Equal(0, vm.MsaaIndex);
        Assert.Equal(new[] { "Off", "2x", "4x", "8x" }, vm.MsaaChoices);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.MsaaIndex = 2;

        Assert.Equal(MsaaMode.X4, vm.MsaaMode);
        Assert.Contains(nameof(PlotterGlSettingsViewModel.MsaaMode), changed);
        Assert.Contains(nameof(PlotterGlSettingsViewModel.MsaaIndex), changed);
        Assert.Equal(
            MsaaMode.X4,
            PlotterGlConfig.Load(settings.Load(), Mill).MsaaMode);

        // 8x rides the same binding: the fourth choice, the fourth member.
        vm.MsaaIndex = 3;

        Assert.Equal(MsaaMode.X8, vm.MsaaMode);
        Assert.Equal(
            MsaaMode.X8,
            PlotterGlConfig.Load(settings.Load(), Mill).MsaaMode);

        // Out-of-range writes — the ComboBox's transient -1 while it has no
        // matching selection, or junk from a stale binding — are ignored: no
        // convert-to-None behind the user's back, nothing new persisted.
        // 4 is now the first index past the members (the guard grew with X8).
        vm.MsaaIndex = -1;
        Assert.Equal(MsaaMode.X8, vm.MsaaMode);
        vm.MsaaIndex = 4;
        Assert.Equal(MsaaMode.X8, vm.MsaaMode);
        vm.MsaaIndex = 9;
        Assert.Equal(MsaaMode.X8, vm.MsaaMode);
        Assert.Equal(
            MsaaMode.X8,
            PlotterGlConfig.Load(settings.Load(), Mill).MsaaMode);
    }

    private static MachineProfile MakeMill(SettingsService settings)
    {
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Mill;
        settings.Save(profile);
        return profile;
    }
}
