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

using CNC.Core.Abstractions;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

/// <summary>
/// The kinematics resolver: preset ⊕ profile ⊕ board letters ⊕ reported count, and the
/// ten invariants of <c>docs/plans/2026-10-14-machine-kinematics.md</c> as data. The
/// bug-report firmware layout (foam plane 2 = A horizontal, Z vertical) is the case
/// the whole plan exists for.
/// </summary>
public class MachineKinematicsResolverTests
{
    private static MachineAxis Axis(int index, AxisDirection direction, AxisKind kind = AxisKind.Linear,
        int sign = 1, double[]? origin = null, char? alias = null) => new()
    {
        Index = index,
        Kind = kind,
        Direction = direction,
        Sign = sign,
        RotationOrigin = origin,
        Alias = alias,
    };

    private static ProfilePlane Plane(params MachineAxis[] axes) => new() { Axes = [.. axes] };

    private static MachineProfile Profile(string machineTypeId, params ProfilePlane[] planes) => new()
    {
        Id = "test",
        Name = "Test",
        MachineTypeId = machineTypeId,
        Planes = [.. planes],
    };

    /// <summary>The bug report: foam firmware that lays plane 2 out as A horizontal
    /// (index 3), Z vertical (index 2) — the inverse of the flattened index order.</summary>
    private static MachineProfile BugFirmwareFoam() => Profile(MachineTypes.Foam,
        Plane(Axis(0, AxisDirection.Length), Axis(1, AxisDirection.Height)),
        Plane(Axis(3, AxisDirection.Length), Axis(2, AxisDirection.Height)));

    /* --------------------------- the motivating bug --------------------------- */

    [Fact]
    public void Bug_firmware_foam_layout_reads_plane_2_as_A_horizontal_Z_vertical()
    {
        var k = MachineKinematicsResolver.Resolve(BugFirmwareFoam(), "XYZA", 4);

        Assert.Equal("XYZA", k.BoardLetters);
        Assert.Equal(4, k.DeclaredAxes);
        Assert.Equal(4, k.ReportedAxes);
        Assert.True(k.IsMultiPlane);
        Assert.True(k.IsValid, string.Join("; ", k.Problems.Select(p => p.Message)));

        // Plane 1: X horizontal (index 0), Y vertical (index 1).
        Assert.Equal(0, k.IndexAt(0, 0));
        Assert.Equal(1, k.IndexAt(0, 1));
        Assert.Equal(0, k.IndexForDirection(0, AxisDirection.Length));
        Assert.Equal(1, k.IndexForDirection(0, AxisDirection.Height));

        // Plane 2: slot 0 = index 3 = A (horizontal), slot 1 = index 2 = Z (vertical).
        Assert.Equal(3, k.IndexAt(1, 0));
        Assert.Equal(2, k.IndexAt(1, 1));
        Assert.Equal('A', k.LetterFor(k.IndexAt(1, 0)));
        Assert.Equal('Z', k.LetterFor(k.IndexAt(1, 1)));
        Assert.Equal(3, k.IndexForDirection(1, AxisDirection.Length));
        Assert.Equal(2, k.IndexForDirection(1, AxisDirection.Height));

        // The plane's indices are deliberately non-monotonic: consumers slice by
        // PlaneIndices/IndexAt and fit by .Max(), never by indices[^1].
        Assert.Equal(new[] { 3, 2 }, k.PlaneIndices(1));
        Assert.Equal(new[] { 0, 1 }, k.PlaneIndices(0));
        Assert.Equal(3, k.PlaneIndices(1).Max());
    }

    [Fact]
    public void IndexAt_PlaneIndices_and_WorldAxisFor_answer_out_of_range_planes_and_slots()
    {
        var k = MachineKinematicsResolver.Resolve(BugFirmwareFoam(), "XYZA", 4);

        Assert.Equal(-1, k.IndexAt(1, 2));
        Assert.Equal(-1, k.IndexAt(0, -1));
        Assert.Equal(-1, k.IndexAt(7, 0));
        Assert.Empty(k.PlaneIndices(7));
        Assert.Empty(k.PlaneIndices(-1));
        Assert.Equal(-1, k.IndexForDirection(9, AxisDirection.Width));

        // Slot k feeds world axis k — positional by construction.
        Assert.Equal(0, k.WorldAxisFor(1, 0));
        Assert.Equal(1, k.WorldAxisFor(1, 1));
        Assert.Equal(-1, k.WorldAxisFor(1, 2));
        Assert.Equal(-1, k.WorldAxisFor(9, 0));

        Assert.Equal(0, k.SeparationFor(-1));
        Assert.Equal(0, k.SeparationFor(9));
    }

    /* ------------------------------- board letters ------------------------------- */

    [Fact]
    public void An_XYUV_board_relabels_the_axes_without_moving_a_single_role()
    {
        // Default foam layout, no profile edit at all: index 2 is still plane 2's
        // horizontal, the board just calls it U.
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Foam), "XYUV", 4);

        Assert.Equal("XYUV", k.BoardLetters);
        Assert.Equal('X', k.LetterFor(0));
        Assert.Equal('U', k.LetterFor(2));
        Assert.Equal('V', k.LetterFor(3));
        Assert.Equal(2, k.IndexAt(1, 0));
        Assert.Equal(3, k.IndexAt(1, 1));
        Assert.Equal(2, k.IndexForDirection(1, AxisDirection.Length));
        Assert.Equal(3, k.IndexForDirection(1, AxisDirection.Height));
        Assert.True(k.IsValid, string.Join("; ", k.Problems.Select(p => p.Message)));
    }

    [Fact]
    public void Without_board_letters_the_conventional_order_is_the_letter_source()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Foam), null, 0);

        Assert.Equal(MachineType.ConventionalLetters, k.BoardLetters);
        Assert.Equal('Z', k.LetterFor(2));
        Assert.Equal('A', k.LetterFor(3));
        Assert.Equal('?', k.LetterFor(9));
        Assert.Equal(0, k.ReportedAxes);
        Assert.False(k.IsPresent(0));
        Assert.Empty(k.Problems);
    }

    /* -------------------------- invariant 10: supersets -------------------------- */

    [Fact]
    public void Declared_axes_past_the_reported_count_are_absent_and_not_an_error()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Foam), "XY", 2);

        Assert.Equal(4, k.DeclaredAxes);
        Assert.Equal(2, k.ReportedAxes);
        Assert.True(k.IsPresent(0));
        Assert.True(k.IsPresent(1));
        Assert.False(k.IsPresent(2));
        Assert.False(k.IsPresent(3));
        Assert.False(k.IsPresent(-1));
        Assert.Empty(k.Problems);
        // The planes still describe the machine; consumers filter by IsPresent.
        Assert.Equal(new[] { 2, 3 }, k.PlaneIndices(1));
    }

    [Fact]
    public void A_disconnected_controller_reports_no_axes_and_no_problems()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Foam), null, -3);

        Assert.Equal(0, k.ReportedAxes);
        Assert.False(k.IsPresent(0));
        Assert.Empty(k.Problems);
    }

    [Fact]
    public void Reported_axes_beyond_the_declared_set_are_surfaced_once_and_stay_out_of_planes()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Mill), "XYZABC", 6);

        var problem = Assert.Single(k.Problems);
        Assert.Equal(KinematicsProblemCodes.ControllerUndeclaredAxes, problem.Code);
        Assert.Equal(KinematicsSeverity.Warning, problem.Severity);
        Assert.Contains("3, 4, 5", problem.Message);
        Assert.True(k.IsValid);          // a warning is tolerated; it is not an error
        Assert.Empty(k.Errors);

        // Undeclared axes are in no plane, but the controller does report them.
        Assert.Equal(new[] { 0, 1, 2 }, k.PlaneIndices(0));
        Assert.Equal(6, k.ReportedAxes);
        Assert.True(k.IsPresent(5));
        Assert.False(k.IsPresent(6));
    }

    /* ------------------------------ rotary (3, 7) ------------------------------ */

    [Fact]
    public void Rotary_axes_live_in_the_optional_axes_and_never_in_a_plane()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes = [Axis(3, AxisDirection.Width, AxisKind.Rotary, origin: [0, 12])];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        Assert.Equal(new[] { 3 }, k.RotaryAxes);
        var axis = Assert.Single(k.OptionalAxes);
        Assert.Equal(AxisKind.Rotary, axis.Kind);
        Assert.Equal(new double[] { 0, 12 }, axis.RotationOrigin);
        Assert.Equal(4, k.DeclaredAxes);       // 3 in the plane + 1 optional
        Assert.Single(k.Planes);
        Assert.DoesNotContain(3, k.PlaneIndices(0));
        Assert.True(k.IsValid, string.Join("; ", k.Problems.Select(p => p.Message)));
    }

    [Fact]
    public void A_rotary_axis_inside_a_plane_is_an_error_but_is_still_listed_as_rotary()
    {
        var profile = Profile(MachineTypes.Mill,
            Plane(Axis(0, AxisDirection.Width),
                  Axis(1, AxisDirection.Length),
                  Axis(2, AxisDirection.Height, AxisKind.Rotary, origin: [0, 0])));

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 3);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.PlaneRotaryAxis, problem.Code);
        Assert.Equal(0, problem.Plane);
        Assert.Equal(2, problem.AxisIndex);
        Assert.Equal(new[] { 2 }, k.RotaryAxes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new double[] { 5 })]
    [InlineData(new double[] { 1, 2, 3 })]
    public void A_rotary_axis_needs_exactly_two_origin_coordinates(double[]? origin)
    {
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes = [Axis(3, AxisDirection.Width, AxisKind.Rotary, origin: origin)];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.RotaryOriginArity, problem.Code);
        Assert.Equal(3, problem.AxisIndex);
        Assert.Equal(-1, problem.Plane);   // an optional axis belongs to no plane
    }

    [Fact]
    public void A_linear_axis_must_not_carry_a_rotation_origin()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes = [Axis(3, AxisDirection.Width, origin: [0, 0])];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.LinearOriginPresent, problem.Code);
        Assert.Equal(3, problem.AxisIndex);
    }

    /* ------------------------------ aliases (8) ------------------------------ */

    [Fact]
    public void An_alias_colliding_with_a_board_letter_is_reported_case_insensitively()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes = [Axis(3, AxisDirection.Width, alias: 'z')];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.AliasBoardLetter, problem.Code);
        Assert.Equal(3, problem.AxisIndex);
    }

    [Fact]
    public void An_alias_collides_with_the_conventional_letters_when_no_board_reported_any()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes = [Axis(3, AxisDirection.Width, alias: 'U')];

        var k = MachineKinematicsResolver.Resolve(profile, null, 0);

        Assert.Equal(KinematicsProblemCodes.AliasBoardLetter, Assert.Single(k.Errors).Code);
    }

    [Fact]
    public void An_alias_free_of_the_board_letters_is_accepted()
    {
        // The legitimate CAM case: a post-processor writes U for the axis the board
        // calls A. Inbound only — it never becomes a second display mapping.
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes = [Axis(3, AxisDirection.Width, alias: 'U')];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        Assert.Empty(k.Problems);
        Assert.Equal("XYZA", k.BoardLetters);
        Assert.Equal('A', k.LetterFor(3));   // still the board's letter, never 'U'
    }

    [Fact]
    public void Two_axes_may_not_share_an_alias()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes =
        [
            Axis(3, AxisDirection.Width, alias: 'U'),
            Axis(4, AxisDirection.Length, alias: 'u'),
        ];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 5);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.AliasDuplicate, problem.Code);
        Assert.Equal(4, problem.AxisIndex);
    }

    /* ---------------------------- separation (4, 6) ---------------------------- */

    [Fact]
    public void Foam_separation_places_only_the_planes_after_the_first()
    {
        var profile = Profile(MachineTypes.Foam);
        profile.Separation = 250;

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        Assert.True(k.IsMultiPlane);
        Assert.Equal(0, k.Planes[0].Separation);
        Assert.Equal(250, k.Planes[1].Separation);
        Assert.Equal(0, k.SeparationFor(0));
        Assert.Equal(250, k.SeparationFor(1));
        Assert.Empty(k.Problems);
    }

    [Fact]
    public void A_single_plane_machine_ignores_and_reports_a_separation()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.Separation = 100;

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 3);

        Assert.False(k.IsMultiPlane);
        Assert.Equal(0, k.SeparationFor(0));
        var problem = Assert.Single(k.Problems);
        Assert.Equal(KinematicsProblemCodes.SeparationUnexpected, problem.Code);
        Assert.Equal(KinematicsSeverity.Warning, problem.Severity);
        Assert.True(k.IsValid);
    }

    /* --------------------------- shape and axes (1, 2, 5, 9) --------------------------- */

    [Fact]
    public void A_profile_must_carry_the_presets_plane_count()
    {
        var k = MachineKinematicsResolver.Resolve(
            Profile(MachineTypes.Foam, Plane(Axis(0, AxisDirection.Length), Axis(1, AxisDirection.Height))),
            "XYZA", 2);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.PlaneCountMismatch, problem.Code);
        Assert.False(k.IsValid);
    }

    [Fact]
    public void A_plane_must_carry_the_presets_axis_count()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Foam,
            Plane(Axis(0, AxisDirection.Length), Axis(1, AxisDirection.Height)),
            Plane(Axis(2, AxisDirection.Length), Axis(3, AxisDirection.Height), Axis(4, AxisDirection.Width))),
            "XYZAB", 5);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.PlaneCountMismatch, problem.Code);
        Assert.Equal(1, problem.Plane);
    }

    [Fact]
    public void A_plane_holds_at_most_three_axes()
    {
        // The preset's own base plane is over-long here: the cap belongs to the
        // resolver, not to the preset.
        var preset = new MachineType
        {
            Id = "TestFourAxisPlane",
            Name = "Four-axis plane",
            BasePlanes = [[AxisDirection.Width, AxisDirection.Length, AxisDirection.Height, AxisDirection.Width]],
        };
        var profile = Profile("TestFourAxisPlane", Plane(
            Axis(0, AxisDirection.Width), Axis(1, AxisDirection.Length),
            Axis(2, AxisDirection.Height), Axis(3, AxisDirection.Width)));

        var k = MachineKinematicsResolver.Resolve(profile, preset, "XYZU", 4);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.PlaneTooManyAxes, problem.Code);
        Assert.Equal(0, problem.Plane);
    }

    [Fact]
    public void An_index_outside_the_grbl_range_is_reported()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Mill,
            Plane(Axis(0, AxisDirection.Width), Axis(1, AxisDirection.Length), Axis(9, AxisDirection.Height))),
            "XYZA", 3);

        Assert.Contains(k.Errors, p => p.Code == KinematicsProblemCodes.AxisIndexRange && p.AxisIndex == 9);
    }

    [Fact]
    public void A_duplicated_index_across_planes_is_reported()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Foam,
            Plane(Axis(0, AxisDirection.Length), Axis(1, AxisDirection.Height)),
            Plane(Axis(3, AxisDirection.Length), Axis(3, AxisDirection.Height))),
            "XYZA", 4);

        Assert.Contains(k.Errors,
            p => p.Code == KinematicsProblemCodes.AxisIndexDuplicate && p.Plane == 1 && p.AxisIndex == 3);
    }

    [Fact]
    public void An_optional_axis_repeating_a_plane_index_is_reported()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.OptionalAxes = [Axis(1, AxisDirection.Height)];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 3);

        Assert.Contains(k.Errors,
            p => p.Code == KinematicsProblemCodes.AxisIndexDuplicate && p.Plane == -1 && p.AxisIndex == 1);
    }

    [Fact]
    public void A_sign_other_than_plus_or_minus_one_is_reported()
    {
        var profile = Profile(MachineTypes.Mill,
            Plane(Axis(0, AxisDirection.Width, sign: 0),
                  Axis(1, AxisDirection.Length, sign: -1),
                  Axis(2, AxisDirection.Height)));

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 3);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.AxisSign, problem.Code);
        Assert.Equal(0, problem.AxisIndex);
    }

    [Fact]
    public void Multi_plane_machines_must_share_their_in_plane_direction_pair()
    {
        var k = MachineKinematicsResolver.Resolve(Profile(MachineTypes.Foam,
            Plane(Axis(0, AxisDirection.Length), Axis(1, AxisDirection.Height)),
            Plane(Axis(2, AxisDirection.Height), Axis(3, AxisDirection.Length))),
            "XYZA", 4);

        var problem = Assert.Single(k.Errors);
        Assert.Equal(KinematicsProblemCodes.PlaneDirectionPair, problem.Code);
        Assert.Equal(1, problem.Plane);
    }

    /* ------------------- pre-migration and hand-edited profiles ------------------- */

    [Fact]
    public void A_profile_with_no_planes_is_seeded_from_the_preset()
    {
        var profile = new MachineProfile { Id = "stale", Name = "Stale", MachineTypeId = MachineTypes.Foam };

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        Assert.Equal(2, k.Planes.Count);
        Assert.Equal(new[] { 0, 1 }, k.PlaneIndices(0));
        Assert.Equal(new[] { 2, 3 }, k.PlaneIndices(1));
        Assert.Equal(AxisDirection.Length, k.Planes[1].Axes[0].Direction);
        Assert.Empty(k.OptionalAxes);
        Assert.True(k.IsValid, string.Join("; ", k.Problems.Select(p => p.Message)));
    }

    [Fact]
    public void A_null_planes_list_reads_as_empty_and_seeds_from_the_preset()
    {
        var profile = new MachineProfile
        {
            Id = "hand-edited",
            Name = "Hand edited",
            MachineTypeId = MachineTypes.Mill,
            Planes = null!,
            OptionalAxes = null!,
        };

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 3);

        Assert.Single(k.Planes);
        Assert.Empty(k.OptionalAxes);
        Assert.Empty(k.Problems);
    }

    [Fact]
    public void A_null_plane_entry_is_reported_rather_than_crashing()
    {
        var profile = new MachineProfile { Id = "hand-edited", Name = "Hand edited", MachineTypeId = MachineTypes.Foam };
        profile.Planes = [null!, Plane(Axis(2, AxisDirection.Length), Axis(3, AxisDirection.Height))];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        Assert.Contains(k.Errors, p => p.Code == KinematicsProblemCodes.PlaneCountMismatch);
        Assert.False(k.IsValid);
        Assert.Empty(k.PlaneIndices(0));
    }

    [Fact]
    public void The_resolved_snapshot_is_not_mutated_by_later_profile_edits()
    {
        var profile = Profile(MachineTypes.Mill,
            Plane(Axis(0, AxisDirection.Width), Axis(1, AxisDirection.Length), Axis(2, AxisDirection.Height)));
        profile.OptionalAxes = [Axis(3, AxisDirection.Width, AxisKind.Rotary, origin: [1, 2])];

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        profile.Planes[0].Axes[0].Index = 7;
        profile.OptionalAxes[0].RotationOrigin![0] = 99;

        Assert.Equal(0, k.IndexAt(0, 0));
        Assert.Equal(1, k.OptionalAxes[0].RotationOrigin![0]);
    }
}

/// <summary>
/// <see cref="SettingsService"/> seeding of the axis layout: a fresh profile starts
/// from the preset, a duplicate copies its source, and a machine-type switch replaces
/// the layout outright (the maintainer's destructive-switch ruling).
/// </summary>
public class MachineProfileKinematicsSeedingTests
{
    private static (string Dir, SettingsService Settings) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-kinematics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    [Fact]
    public void A_fresh_profile_seeds_its_layout_from_the_preset()
    {
        var (_, settings) = MakeSettings();

        var foam = settings.CreateProfile("foam", MachineTypes.Foam);
        var mill = settings.CreateProfile("mill", MachineTypes.Mill);

        Assert.Equal(2, foam.Planes.Count);
        Assert.Equal(new[] { 0, 1 }, foam.Planes[0].Axes.Select(a => a.Index));
        Assert.Equal(new[] { 2, 3 }, foam.Planes[1].Axes.Select(a => a.Index));
        Assert.All(foam.Planes, p => Assert.Equal(
            new[] { AxisDirection.Length, AxisDirection.Height }, p.Axes.Select(a => a.Direction)));
        Assert.Empty(foam.OptionalAxes);
        Assert.Equal(0, foam.Separation);          // the preset's DefaultSeparation
        Assert.Equal(4, foam.DemoAxes);

        Assert.Single(mill.Planes);
        Assert.Equal(new[] { 0, 1, 2 }, mill.Planes[0].Axes.Select(a => a.Index));
        Assert.Equal(3, mill.DemoAxes);
    }

    [Fact]
    public void A_duplicate_copies_its_source_layout_instead_of_re_seeding_it()
    {
        var (_, settings) = MakeSettings();
        var foam = settings.CreateProfile("foam", MachineTypes.Foam);
        foam.Planes[1].Axes[0].Index = 3;   // the bug-firmware layout
        foam.Planes[1].Axes[1].Index = 2;
        foam.Separation = 250;
        foam.OptionalAxes = [new MachineAxis
        {
            Index = 4,
            Kind = AxisKind.Rotary,
            Direction = AxisDirection.Width,
            RotationOrigin = [0, 0],
        }];
        settings.SaveProfile(foam);

        var copy = settings.CreateProfile("foam copy", MachineTypes.Mill, copyFromId: foam.Id);

        Assert.Equal("foam-copy", copy.Id);
        Assert.Equal(MachineTypes.Foam, copy.MachineTypeId);   // the source's type, not the argument
        Assert.Equal(new[] { 3, 2 }, copy.Planes[1].Axes.Select(a => a.Index));
        Assert.Equal(250, copy.Separation);
        Assert.Equal(4, Assert.Single(copy.OptionalAxes).Index);
    }

    [Fact]
    public void A_machine_type_switch_replaces_the_layout_outright()
    {
        var (_, settings) = MakeSettings();
        var foam = settings.CreateProfile("foam", MachineTypes.Foam);
        foam.Planes[1].Axes[0].Index = 3;
        foam.Planes[1].Axes[1].Index = 2;
        foam.Separation = 250;
        foam.OptionalAxes = [new MachineAxis { Index = 4, Kind = AxisKind.Rotary, Direction = AxisDirection.Width, RotationOrigin = [0, 0] }];
        settings.SaveProfile(foam);

        settings.SetProfileMachineType(foam.Id, MachineTypes.Mill);

        var switched = settings.AllProfiles().Single(p => p.Id == "foam");
        Assert.Equal(MachineTypes.Mill, switched.MachineTypeId);
        Assert.Single(switched.Planes);
        Assert.Equal(new[] { 0, 1, 2 }, switched.Planes[0].Axes.Select(a => a.Index));
        Assert.Empty(switched.OptionalAxes);
        Assert.Equal(0, switched.Separation);
    }

    [Fact]
    public void A_profile_file_written_before_the_axis_model_still_resolves()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-old-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "profiles"));
        // The exact shape a pre-2026-10 profile file has: no Planes/OptionalAxes/Separation.
        File.WriteAllText(Path.Combine(dir, "profiles", "old.json"),
            """{"Id":"old","Name":"Old foam","MachineTypeId":"Foam","DemoAxes":4}""");

        var profile = new SettingsService(dir).Load();

        Assert.Equal("old", profile.Id);
        Assert.Empty(profile.Planes);        // absent in the file, so "seed me"
        Assert.Empty(profile.OptionalAxes);
        Assert.Equal(0, profile.Separation);

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", profile.DemoAxes);
        Assert.True(k.IsMultiPlane);
        Assert.Equal(new[] { 0, 1 }, k.PlaneIndices(0));
        Assert.Equal(new[] { 2, 3 }, k.PlaneIndices(1));
        Assert.True(k.IsValid, string.Join("; ", k.Problems.Select(p => p.Message)));
    }
}

/// <summary>
/// The provider seam: it owns the merge of the active profile with the live
/// controller, so it is the one place that has to notice a connect (letters/count from
/// <c>[AXS:]</c>) and a profile switch.
/// </summary>
public class MachineKinematicsProviderTests
{
    private static (string Dir, SettingsService Settings) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-kinematics-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    private sealed class FakeGrbl : IGrblService
    {
        public bool IsConnected { get; private set; }
        public ControllerCapabilities Capabilities { get; set; } = ControllerCapabilities.Unknown;
        public event EventHandler? ConnectionChanged;

        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Empty<MachineState>();

        /// <summary>Connect synchronously — the connect dialog's outcome, without a
        /// blocking wait on a task the test would otherwise have to await.</summary>
        public void Connect()
        {
            IsConnected = true;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Disconnect()
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task ConnectAsync(string port)
        {
            Connect();
            return Task.CompletedTask;
        }

        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    [Fact]
    public void It_resolves_on_first_access_and_again_on_connect()
    {
        var (_, settings) = MakeSettings();
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);
        var changes = 0;
        provider.Changed += (_, _) => changes++;

        var before = provider.Current;
        Assert.Equal(0, before.ReportedAxes);
        Assert.Equal(MachineType.ConventionalLetters, before.BoardLetters);

        // The handshake lands capabilities first, then raises the connect.
        grbl.Capabilities = ControllerCapabilities.Full with { NumAxes = 4, AxisLetters = "XYUV" };
        grbl.Connect();

        var after = provider.Current;
        Assert.NotSame(before, after);
        Assert.Equal(4, after.ReportedAxes);
        Assert.Equal("XYUV", after.BoardLetters);
        Assert.Equal('U', after.LetterFor(2));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void It_publishes_a_profile_switch_even_between_identically_shaped_profiles()
    {
        var (_, settings) = MakeSettings();
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);
        var first = provider.Current;
        Assert.Equal(MachineProfile.DefaultId, first.ProfileId);

        var changes = 0;
        provider.Changed += (_, _) => changes++;

        var second = settings.CreateProfile("second", MachineTypes.Mill);
        settings.Activate(second.Id);

        Assert.Equal("second", provider.Current.ProfileId);
        Assert.NotSame(first, provider.Current);
        // Activate raises Changed and then ProfileSwitched; only one publish follows.
        Assert.Equal(1, changes);
    }

    [Fact]
    public void It_ignores_a_settings_save_that_does_not_change_the_kinematics()
    {
        var (_, settings) = MakeSettings();
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);
        _ = provider.Current;

        var changes = 0;
        provider.Changed += (_, _) => changes++;

        var profile = settings.Load();
        profile.VisibleTools = 12;
        settings.Save(profile);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Refresh_publishes_an_axis_edit_and_then_stays_quiet()
    {
        var (_, settings) = MakeSettings();
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);

        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Foam;
        profile.Planes = MachineTypeCatalog.Resolve(MachineTypes.Foam).CreateDefaultPlanes();
        profile.DemoAxes = 4;
        settings.Save(profile);
        Assert.True(provider.Current.IsMultiPlane);

        var changes = 0;
        provider.Changed += (_, _) => changes++;

        // Re-author plane 2 the way the bug-report firmware expects: A first, Z second.
        profile.Planes[1].Axes[0].Index = 3;
        profile.Planes[1].Axes[1].Index = 2;
        provider.Refresh();

        Assert.Equal(1, changes);
        Assert.Equal(new[] { 3, 2 }, provider.Current.PlaneIndices(1));
        Assert.Equal('A', provider.Current.LetterFor(provider.Current.IndexAt(1, 0)));

        provider.Refresh();   // unchanged: no republish, and the instance is kept
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ApplyType_on_the_active_profile_reaches_the_provider()
    {
        var (_, settings) = MakeSettings();
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);
        var profile = provider.Current;
        Assert.False(profile.IsMultiPlane);

        var changes = 0;
        provider.Changed += (_, _) => changes++;

        settings.SetProfileMachineType(profile.ProfileId, MachineTypes.Foam);

        Assert.Equal(1, changes);
        Assert.True(provider.Current.IsMultiPlane);
        Assert.Equal(4, provider.Current.DeclaredAxes);
    }

    [Fact]
    public void Dispose_unsubscribes_from_the_settings_and_connection_events()
    {
        var (_, settings) = MakeSettings();
        var grbl = new FakeGrbl();
        var provider = new MachineKinematicsProvider(settings, grbl);
        var changes = 0;
        provider.Changed += (_, _) => changes++;

        provider.Dispose();

        grbl.Connect();
        settings.Save(settings.Load());

        Assert.Equal(0, changes);
    }
}
