
/*
 * JogRoleAwareCrossTests.cs - jog's axis cross is driven and labelled by role,
 * not by slot position.
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
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

/// <summary>
/// Role-aware jog cross. The bug report: firmware assumes plane 2 = <c>A</c>
/// horizontal, <c>Z</c> vertical, so the profile's plane-2 slots are indices
/// <c>[3, 2]</c> — non-monotonic. The old cross read <c>LetterFor(2)</c> /
/// <c>LetterFor(3)</c>, i.e. slot position, which labels and drives it as
/// <c>Z</c> horizontal / <c>A</c> vertical: the exact inverse.
/// </summary>
[Collection("LocalizationState")]
public class JogRoleAwareCrossTests
{
    private static CNC.Core.GrblInfoState Info(string letters, int numAxes = 4) =>
        new() { AxisLetters = letters, NumAxes = numAxes };

    /// <summary>Foam settings whose second plane is bound to explicit indices —
    /// the bug firmware's <c>[3, 2]</c> is <see cref="FoamSettings"/> with 3/2.</summary>
    private static SettingsService FoamSettings(int plane2Horizontal, int plane2Vertical)
    {
        var settings = JogViewModelTests.MakeSettings("Foam");
        var profile = settings.Load();
        var planes = MachineTypeCatalog.Resolve(MachineTypes.Foam).CreateDefaultPlanes();
        planes[1].Axes[0].Index = plane2Horizontal;
        planes[1].Axes[1].Index = plane2Vertical;
        profile.Planes = planes;
        settings.Save(profile);
        return settings;
    }

    [Fact]
    public void Bug_firmware_plane2_labels_and_drives_by_role_not_slot_position()
    {
        // THE regression: plane 2 slots [3, 2] on an XYZA board. Plane-2 tags must
        // read A (horizontal) / Z (vertical), and a plane-2 horizontal jog must emit
        // an A word — the vertical motor must never move for a horizontal press.
        var settings = FoamSettings(plane2Horizontal: 3, plane2Vertical: 2);
        var sent = new List<string>();
        var vm = new JogViewModel(
            new JogViewModelTests.RecordingGrbl(sent, numAxes: 4), settings, Info("XYZA"));

        Assert.Equal("X", vm.P1H);
        Assert.Equal("Y", vm.P1V);
        Assert.Equal("A", vm.P2H);
        Assert.Equal("Z", vm.P2V);
        Assert.True(vm.Plane2Visible);
        Assert.Equal("A+", vm.SecondRightTag);
        Assert.Equal("Z+", vm.SecondUpTag);

        vm.JogCommand.Execute(vm.SecondRightTag);

        Assert.Equal(["$J=G91 A1 F1000"], sent);
        Assert.DoesNotContain(sent, line => line.Contains('Z'));
    }

    [Fact]
    public void Mill_cross_is_the_two_in_plane_slots_and_depth_keeps_a_linear_row()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl(sent, numAxes: 4),
            JogViewModelTests.MakeSettings());

        Assert.Equal("X", vm.P1H);
        Assert.Equal("Y", vm.P1V);
        Assert.False(vm.IsMultiPlane);
        Assert.False(vm.Plane2Visible);
        // The mill's depth (index 2) is not an in-plane cross slot: it is a linear row.
        Assert.Equal(["Z", "A"], vm.LinearAxisLetters.Select(r => r.Letter));
        Assert.Equal("X-,Y+", vm.UpLeftTag);

        vm.JogCommand.Execute(vm.UpLeftTag);

        Assert.Equal(["$J=G91 X-1 Y1 F1000"], sent);
    }

    [Fact]
    public void Plane_two_is_hidden_when_its_highest_slot_is_absent()
    {
        // [3, 2] on a 3-axis board: index 2 is reported but index 3 is not, so the
        // cross would drive a missing motor — hidden (fit by max index, not count).
        var settings = FoamSettings(plane2Horizontal: 3, plane2Vertical: 2);
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 3),
            settings, Info("XYZA", numAxes: 3));

        Assert.True(vm.IsMultiPlane);
        Assert.False(vm.Plane2Visible);
        Assert.True(vm.Plane2HiddenHintVisible);
        Assert.False(vm.TogetherVisible);
        // Z (index 2) is still reachable as a linear row.
        Assert.Equal(["Z"], vm.LinearAxisLetters.Select(r => r.Letter));
        Assert.Equal("X+", vm.RightTag);
    }

    [Fact]
    public void Together_is_offered_only_for_visible_planes_sharing_their_direction_pair()
    {
        // Parallel towers (the hot wire): both visible, same in-plane pair.
        var parallel = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 4),
            JogViewModelTests.MakeSettings("Foam"), Info("XYZA"));
        Assert.True(parallel.TogetherVisible);
        Assert.Equal("X+|Z+", parallel.RightTag);

        // Same layout, but plane 2 no longer spans plane 1's pair: nothing to mirror,
        // so the padlock is off and the plane-1 tags carry no coupled '|' part.
        var settings = JogViewModelTests.MakeSettings("Foam");
        var profile = settings.Load();
        var planes = MachineTypeCatalog.Resolve(MachineTypes.Foam).CreateDefaultPlanes();
        planes[1].Axes[0].Direction = AxisDirection.Width;
        profile.Planes = planes;
        settings.Save(profile);
        var skew = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 4),
            settings, Info("XYZA"));

        Assert.True(skew.Plane2Visible);
        Assert.False(skew.TogetherVisible);
        Assert.Equal("X+", skew.RightTag);
    }

    [Fact]
    public void Optional_rotary_axis_keeps_a_jog_row()
    {
        // A rotary axis lives outside every plane; it must not be silently dropped
        // from jog (the mill's depth and the rotary each get a +/- row).
        var settings = JogViewModelTests.MakeSettings("Mill");
        var profile = settings.Load();
        profile.Planes = MachineTypeCatalog.Resolve(MachineTypes.Mill).CreateDefaultPlanes();
        profile.OptionalAxes =
        [
            new MachineAxis
            {
                Index = 3,
                Kind = AxisKind.Rotary,
                Direction = AxisDirection.Width,
                RotationOrigin = [0d, 0d],
            },
        ];
        settings.Save(profile);

        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 4),
            settings, Info("XYZA"));

        Assert.Equal(["Z", "A"], vm.LinearAxisLetters.Select(r => r.Letter));
        Assert.Equal("A+", vm.LinearAxisLetters[1].PlusTag);
    }
}
