/*
 * TransformProgramViewModelsTests.cs
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

using CNC.Core.Services;
using CNC.Core.ViewModels;
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

public class TransformProgramViewModelsTests
{
    [Fact]
    public void RotateDialog_defaults_to_zero_origin_and_90_degrees()
    {
        var vm = new RotateProgramViewModel();

        Assert.Equal(90d, vm.Angle);
        Assert.Equal(0d, vm.OriginX);
        Assert.Equal(0d, vm.OriginY);
    }

    [Fact]
    public void RotateDialog_carries_the_dro_position_as_origin_default()
    {
        var vm = new RotateProgramViewModel(-12.5, 38.25);

        Assert.Equal(-12.5, vm.OriginX);
        Assert.Equal(38.25, vm.OriginY);
    }

    [Fact]
    public void RotateDialog_ok_closes_true_and_cancel_closes_false()
    {
        var vm = new RotateProgramViewModel(1, 2);
        bool? result = null;

        vm.CloseRequested += (_, r) => result = r;
        vm.OkCommand.Execute(null);
        Assert.True(result);

        vm.CancelCommand.Execute(null);
        Assert.False(result);
    }

    [Fact]
    public void ArcsToLinesDialog_defaults_tolerance_to_point_one()
    {
        var vm = new ArcsToLinesProgramViewModel();

        Assert.Equal(0.1d, vm.Tolerance);
    }

    [Fact]
    public void ArcsToLinesDialog_ok_closes_true_with_edited_tolerance()
    {
        var vm = new ArcsToLinesProgramViewModel();
        bool? result = null;

        vm.CloseRequested += (_, r) => result = r;
        vm.Tolerance = 0.05d;
        vm.OkCommand.Execute(null);

        Assert.True(result);
        Assert.Equal(0.05d, vm.Tolerance);
    }

    [Fact]
    public void ProgramConfigStore_round_trips_comment_mode()
    {
        var profile = new MachineProfile();

        var config = new ProgramConfig { CompressCommentMode = GCodeCommentMode.StripAll };
        ProgramConfigStore.Save(profile, config);

        Assert.Equal(GCodeCommentMode.StripAll, ProgramConfigStore.Load(profile).CompressCommentMode);
        Assert.Equal(GCodeCommentMode.Keep, ProgramConfigStore.Load(new MachineProfile()).CompressCommentMode);
    }

    [Fact]
    public void ProgramConfigStore_accepts_legacy_dictionary_shape()
    {
        var profile = new MachineProfile();
        profile.ModuleSettings["Program"] = new Dictionary<string, object>
        {
            ["CompressCommentMode"] = "StripAll",
        };

        Assert.Equal(GCodeCommentMode.StripAll, ProgramConfigStore.Load(profile).CompressCommentMode);
    }

    /// <summary>Resolved kinematics for a machine type with the given optional axes —
    /// a rotary axis is an OPTIONAL axis (it never sits in a plane), which is exactly
    /// why a foam profile's fourth axis is not one.</summary>
    private static MachineKinematics Kinematics(string machineTypeId, int reportedAxes,
        string boardLetters, params MachineAxis[] optionalAxes)
    {
        var preset = MachineTypeCatalog.Resolve(machineTypeId);
        var profile = new MachineProfile
        {
            Id = "wrap-" + machineTypeId,
            MachineTypeId = machineTypeId,
            Planes = preset.CreateDefaultPlanes(),
            OptionalAxes = [.. optionalAxes],
        };
        return MachineKinematicsResolver.Resolve(profile, preset, boardLetters, reportedAxes);
    }

    private static MachineKinematics Kinematics(string machineTypeId, int reportedAxes = 5,
        params MachineAxis[] optionalAxes) =>
        Kinematics(machineTypeId, reportedAxes, MachineType.ConventionalLetters, optionalAxes);

    private static MachineAxis Rotary(int index) => new()
    {
        Index = index,
        Kind = AxisKind.Rotary,
        Direction = AxisDirection.Length,
        RotationOrigin = [0d, 0d],
    };

    [Fact]
    public void WrapDialog_offers_the_declared_linear_axes_and_the_rotary_target()
    {
        var vm = new WrapProgramViewModel(WrapDirection.Wrap, Kinematics(MachineTypes.Mill, 4, Rotary(3)));

        Assert.Equal([0, 1, 2], vm.SourceAxes.Select(a => a.Index));
        Assert.Equal([3], vm.TargetAxes.Select(a => a.Index));
        Assert.Equal(1, vm.SourceAxis!.Index); // defaults to Y
        Assert.Equal(3, vm.TargetAxis!.Index); // defaults to A
        Assert.True(vm.IsWrap);
    }

    [Fact]
    public void UnwrapDialog_offers_the_rotary_sources_and_the_declared_linear_targets()
    {
        var vm = new WrapProgramViewModel(WrapDirection.Unwrap, Kinematics(MachineTypes.Mill, 4, Rotary(3)));

        Assert.Equal([3], vm.SourceAxes.Select(a => a.Index));
        Assert.Equal([0, 1, 2], vm.TargetAxes.Select(a => a.Index));
        Assert.Equal(3, vm.SourceAxis!.Index);
        Assert.Equal(0, vm.TargetAxis!.Index);
        Assert.False(vm.IsWrap);
    }

    [Fact]
    public void WrapDialog_offers_exactly_the_declared_rotaries_not_the_reported_count()
    {
        var one = new WrapProgramViewModel(WrapDirection.Wrap, Kinematics(MachineTypes.Mill, 5, Rotary(3)));
        var two = new WrapProgramViewModel(WrapDirection.Wrap, Kinematics(MachineTypes.Mill, 5, Rotary(3), Rotary(4)));
        // Declared but not reported: still the profile's rotary axis, offered by kind
        // (the reported count is a presence fact, never a kind fact).
        var declaredPastTheBoard = new WrapProgramViewModel(WrapDirection.Wrap, Kinematics(MachineTypes.Mill, 3, Rotary(3)));

        Assert.Equal([3], one.TargetAxes.Select(a => a.Index));
        Assert.Equal([3, 4], two.TargetAxes.Select(a => a.Index));
        Assert.Equal([3], declaredPastTheBoard.TargetAxes.Select(a => a.Index));
    }

    [Fact]
    public void Foam_profile_offers_no_rotary_axis_to_wrap_onto_or_unwrap_from()
    {
        // The live bug: foam runs 4 axes, but all four are linear tower axes.
        var foam = Kinematics(MachineTypes.Foam, 4);
        Assert.Empty(foam.RotaryAxes);

        var wrap = new WrapProgramViewModel(WrapDirection.Wrap, foam);
        Assert.Empty(wrap.TargetAxes);
        Assert.Equal([0, 1, 2, 3], wrap.SourceAxes.Select(a => a.Index));
        Assert.Null(wrap.TargetAxis);

        var unwrap = new WrapProgramViewModel(WrapDirection.Unwrap, foam);
        Assert.Empty(unwrap.SourceAxes);
        Assert.Equal([0, 1, 2, 3], unwrap.TargetAxes.Select(a => a.Index));
        Assert.Null(unwrap.SourceAxis);
    }

    [Fact]
    public void WrapDialog_axis_labels_follow_the_boards_letters()
    {
        // A remapped board: index 2 is the board's U and index 3 its V, so the
        // labels are the board's, never the conventional XYZABC order.
        var kinematics = Kinematics(MachineTypes.Mill, 4, "XYUV", Rotary(3));

        var vm = new WrapProgramViewModel(WrapDirection.Wrap, kinematics);

        Assert.Equal(["X-axis", "Y-axis", "U-axis"], vm.SourceAxes.Select(a => a.Label));
        Assert.Equal(["V-axis"], vm.TargetAxes.Select(a => a.Label));
    }

    [Fact]
    public void WrapDialog_ok_closes_true_and_cancel_closes_false()
    {
        var vm = new WrapProgramViewModel(WrapDirection.Wrap, Kinematics(MachineTypes.Mill, 4, Rotary(3)));
        bool? result = null;

        vm.CloseRequested += (_, r) => result = r;
        vm.OkCommand.Execute(null);
        Assert.True(result);

        vm.CancelCommand.Execute(null);
        Assert.False(result);
    }

    [Fact]
    public void WrapDialog_ok_is_disabled_until_the_diameter_is_positive()
    {
        var vm = new WrapProgramViewModel(WrapDirection.Wrap, Kinematics(MachineTypes.Mill, 4, Rotary(3)));

        Assert.True(vm.OkCommand.CanExecute(null)); // defaults to 50

        vm.Diameter = 0d;
        Assert.False(vm.OkCommand.CanExecute(null));

        vm.Diameter = -12.5d;
        Assert.False(vm.OkCommand.CanExecute(null));

        vm.Diameter = 25d;
        Assert.True(vm.OkCommand.CanExecute(null));
    }

    [Fact]
    public void WrapDialog_builds_axis_labels_through_the_injected_formatter()
    {
        var localized = new WrapProgramViewModel(WrapDirection.Wrap,
            Kinematics(MachineTypes.Mill, 5, Rotary(3), Rotary(4)),
            letter => "axis:" + letter);

        Assert.Equal(["axis:X", "axis:Y", "axis:Z"], localized.SourceAxes.Select(a => a.Label));
        Assert.Equal(["axis:A", "axis:B"], localized.TargetAxes.Select(a => a.Label));

        // No formatter (tests, non-localized hosts): the English default.
        var fallback = new WrapProgramViewModel(WrapDirection.Unwrap,
            Kinematics(MachineTypes.Mill, 4, Rotary(3)));
        Assert.Equal("A-axis", fallback.SourceAxes.Single().Label);
        Assert.Equal(["X-axis", "Y-axis", "Z-axis"], fallback.TargetAxes.Select(a => a.Label));
    }
}
