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

namespace CNC.Core.Services;

/// <summary>
/// Resolves a machine profile into one <see cref="MachineKinematics"/>:
/// <b>preset ⊕ profile ⊕ board letters ⊕ reported axis count</b>.
/// <para>
/// It is a pure function — no I/O, no controller — so the profile editor can call it
/// to validate a draft, and the app's provider can call it on connect and on a
/// profile switch. Every invariant (1–10 in
/// <c>docs/plans/2026-10-14-machine-kinematics.md</c>) is checked here and returned
/// as <see cref="KinematicsProblem"/> data, never as an exception: a hand-edited
/// profile file must not crash the app, it must render.
/// </para>
/// <para>
/// A profile whose <see cref="MachineProfile.Planes"/> are empty (written before the
/// axis model existed, or explicitly cleared) is seeded from the preset's
/// <see cref="MachineType.BasePlanes"/> with the default conventional indices, so it
/// behaves exactly like a freshly created profile. <b>This is why the profile format
/// carries no schema-version field.</b> A file's shape is decided by the structure it
/// actually has, and the loader already refuses the one failure a version number
/// would catch: <c>AppSettingsBundle.TryDeserialize</c> rejects a payload that binds
/// only case-insensitively, which is exactly a profile that would otherwise read as
/// defaults. Adding a version would put a second source of truth next to this check.
/// The one field it cannot interpret is <see cref="MachineProfile.Separation"/> —
/// a non-nullable double, so "absent" and "explicitly 0" are indistinguishable, and
/// a pre-model foam profile therefore starts at 0 until the machine card seeds the
/// preset's value. That is an old file carrying no separation, not a misread.
/// </para>
/// </summary>
public static class MachineKinematicsResolver
{
    /// <summary>Resolve against the preset named by the profile's
    /// <see cref="MachineProfile.MachineTypeId"/> (unknown ids fall back to Mill, like
    /// <see cref="MachineTypeCatalog.Resolve"/>).</summary>
    /// <param name="profile">The machine profile to resolve.</param>
    /// <param name="boardLetters">The controller's <c>[AXS:]</c> letters, or null/empty
    /// when it reported none — then the conventional <c>XYZABCUVW</c> order applies.</param>
    /// <param name="reportedAxes">Axes the controller reports; <c>0</c> = none/disconnected.</param>
    public static MachineKinematics Resolve(MachineProfile profile, string? boardLetters, int reportedAxes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Resolve(profile, MachineTypeCatalog.Resolve(profile.MachineTypeId), boardLetters, reportedAxes);
    }

    /// <summary>Resolve against an explicit preset (the profile-editor and test path —
    /// a draft profile may not be in the catalog yet).</summary>
    public static MachineKinematics Resolve(MachineProfile profile, MachineType preset,
                                            string? boardLetters, int reportedAxes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(preset);

        var problems = new List<KinematicsProblem>();
        var basePlanes = preset.BasePlanes;

        // The profile's planes, or the preset's defaults when it carries none — a
        // profile file created before the axis model must not crash and must behave
        // like a fresh profile rather than as a zero-axis machine.
        var planes = NormalizePlanes(profile.Planes);
        if (planes.Count == 0)
            planes = preset.CreateDefaultPlanes();
        var optionalAxes = profile.OptionalAxes.Where(a => a is not null).ToList();

        var separation = profile.Separation;
        var all = CollectAxes(planes, optionalAxes);

        ValidateShape(preset, basePlanes, planes, problems);
        ValidatePlanes(planes, problems);
        ValidateAxes(all, problems);

        var letters = string.IsNullOrEmpty(boardLetters) ? MachineType.ConventionalLetters : boardLetters;
        ValidateAliases(all, letters, problems);
        ValidateSeparation(planes.Count, separation, problems);
        ValidateReportedAxes(all, reportedAxes, problems);

        var resolvedPlanes = new List<KinematicPlane>(planes.Count);
        for (var p = 0; p < planes.Count; p++)
        {
            // Plane 0 is the machine's datum (separation 0); later planes sit at the
            // profile's separation along the direction no axis of theirs spans.
            var planeSeparation = p == 0 ? 0 : separation;
            resolvedPlanes.Add(new KinematicPlane(planes[p].Axes.Select(Copy).ToArray(), planeSeparation));
        }

        var rotaryAxes = all
            .Where(a => a.Axis.Kind == AxisKind.Rotary)
            .Select(a => a.Axis.Index)
            .Distinct()
            .OrderBy(i => i)
            .ToArray();

        return new MachineKinematics(
            profile.Id,
            preset.Id,
            resolvedPlanes,
            optionalAxes.Select(Copy).ToArray(),
            all.Count,
            Math.Max(0, reportedAxes),
            rotaryAxes,
            letters,
            problems);
    }

    // Invariant 5: the preset locks the number of planes and each plane's axis count.
    private static void ValidateShape(MachineType preset,
        IReadOnlyList<IReadOnlyList<AxisDirection>> basePlanes,
        IReadOnlyList<ProfilePlane> planes,
        List<KinematicsProblem> problems)
    {
        if (basePlanes.Count == 0)
            return;

        if (planes.Count != basePlanes.Count)
        {
            problems.Add(Error(KinematicsProblemCodes.PlaneCountMismatch,
                $"Machine type '{preset.Id}' defines {basePlanes.Count} plane(s); the profile defines {planes.Count}."));
            return;
        }

        for (var p = 0; p < planes.Count; p++)
        {
            if (planes[p].Axes.Count != basePlanes[p].Count)
            {
                problems.Add(Error(KinematicsProblemCodes.PlaneCountMismatch,
                    $"Plane {p + 1} of machine type '{preset.Id}' must have an axis count of " +
                    $"{basePlanes[p].Count}; the profile declares {planes[p].Axes.Count}.", plane: p));
            }
        }
    }

    // Invariants 2, 3 and 4.
    private static void ValidatePlanes(IReadOnlyList<ProfilePlane> planes, List<KinematicsProblem> problems)
    {
        for (var p = 0; p < planes.Count; p++)
        {
            var axes = planes[p].Axes;
            if (axes.Count > 3)
            {
                problems.Add(Error(KinematicsProblemCodes.PlaneTooManyAxes,
                    $"Plane {p + 1} holds {axes.Count} axes; a plane holds at most 3 " +
                    "(horizontal, vertical, perpendicular).", plane: p));
            }

            foreach (var axis in axes)
            {
                if (axis.Kind == AxisKind.Rotary)
                {
                    problems.Add(Error(KinematicsProblemCodes.PlaneRotaryAxis,
                        $"Index {axis.Index} is rotary; a rotary axis spins about a line rather than " +
                        "sitting in a plane, so it belongs in the optional axes.", plane: p, axisIndex: axis.Index));
                }
            }
        }

        if (planes.Count < 2)
            return;

        // Parallel planes are coupled: they must span the same in-plane direction pair.
        var reference = InPlanePair(planes[0]);
        for (var p = 1; p < planes.Count; p++)
        {
            var pair = InPlanePair(planes[p]);
            if (!pair.SequenceEqual(reference))
            {
                problems.Add(Error(KinematicsProblemCodes.PlaneDirectionPair,
                    $"Plane {p + 1} spans {Describe(pair)} but plane 1 spans {Describe(reference)}; " +
                    "the planes of a multi-plane machine must share their in-plane direction pair.",
                    plane: p));
            }
        }

        static AxisDirection[] InPlanePair(ProfilePlane plane) =>
            plane.Axes.Take(2).Select(a => a.Direction).ToArray();
    }

    // Invariants 1, 7 and 9 over every axis (planes first, then optional axes).
    private static void ValidateAxes(List<(MachineAxis Axis, int Plane)> all, List<KinematicsProblem> problems)
    {
        var seen = new Dictionary<int, MachineAxis>();
        foreach (var (axis, plane) in all)
        {
            if (axis.Index < 0 || axis.Index > 8)
            {
                problems.Add(Error(KinematicsProblemCodes.AxisIndexRange,
                    $"Axis index {axis.Index} is outside the grbl range 0..8.", plane: plane, axisIndex: axis.Index));
            }
            else if (!seen.TryAdd(axis.Index, axis))
            {
                problems.Add(Error(KinematicsProblemCodes.AxisIndexDuplicate,
                    $"Axis index {axis.Index} is declared more than once; every index may appear only " +
                    "once across the planes and the optional axes.", plane: plane, axisIndex: axis.Index));
            }

            if (axis.Sign != 1 && axis.Sign != -1)
            {
                problems.Add(Error(KinematicsProblemCodes.AxisSign,
                    $"Axis index {axis.Index} has sign {axis.Sign}; the sign must be +1 or -1.",
                    plane: plane, axisIndex: axis.Index));
            }

            if (axis.Kind == AxisKind.Rotary)
            {
                if (axis.RotationOrigin is not { Length: 2 })
                {
                    problems.Add(Error(KinematicsProblemCodes.RotaryOriginArity,
                        $"Rotary axis {axis.Index} needs a rotation origin of exactly two coordinates " +
                        "(the two perpendicular to its direction).", plane: plane, axisIndex: axis.Index));
                }
            }
            else if (axis.RotationOrigin is not null)
            {
                problems.Add(Error(KinematicsProblemCodes.LinearOriginPresent,
                    $"Linear axis {axis.Index} carries a rotation origin; only rotary axes have a pivot.",
                    plane: plane, axisIndex: axis.Index));
            }
        }
    }

    // Invariant 8: an alias must be reachable, so it collides with neither the board's
    // letters (the conventional set when it reported none) nor another alias.
    private static void ValidateAliases(List<(MachineAxis Axis, int Plane)> all, string letters,
        List<KinematicsProblem> problems)
    {
        var seen = new HashSet<char>();
        var boardLetters = letters.ToUpperInvariant();
        foreach (var (axis, plane) in all)
        {
            if (axis.Alias is not { } alias)
                continue;

            var upper = char.ToUpperInvariant(alias);
            if (boardLetters.Contains(upper))
            {
                problems.Add(Error(KinematicsProblemCodes.AliasBoardLetter,
                    $"The CAM alias '{upper}' on axis index {axis.Index} is already one of the board's " +
                    $"letters ({letters}); a program letter resolves through the board first, so the " +
                    "alias could never be reached.", plane: plane, axisIndex: axis.Index));
            }
            else if (!seen.Add(upper))
            {
                problems.Add(Error(KinematicsProblemCodes.AliasDuplicate,
                    $"The CAM alias '{upper}' is used by more than one axis.", plane: plane, axisIndex: axis.Index));
            }
        }
    }

    // Invariant 6: only a multi-plane machine has a plane-to-plane distance to carry.
    private static void ValidateSeparation(int planeCount, double separation, List<KinematicsProblem> problems)
    {
        if (planeCount <= 1 && separation != 0)
        {
            problems.Add(Warning(KinematicsProblemCodes.SeparationUnexpected,
                $"This machine has a single plane, so the separation of {separation} is ignored; " +
                "a plane-to-plane distance needs two planes."));
        }
    }

    // Invariant 10: superset tolerance, both ways. Declared axes past the reported
    // count are simply absent (IsPresent) — not an error. Indices the controller
    // reports that the profile never declares are surfaced once here and are not in
    // any plane (planes come from the profile, so they cannot be).
    private static void ValidateReportedAxes(List<(MachineAxis Axis, int Plane)> all, int reportedAxes,
        List<KinematicsProblem> problems)
    {
        if (reportedAxes <= 0)
            return;

        var declared = new HashSet<int>(all.Select(a => a.Axis.Index));
        var undeclared = Enumerable.Range(0, reportedAxes).Where(i => !declared.Contains(i)).ToArray();
        if (undeclared.Length == 0)
            return;

        problems.Add(Warning(KinematicsProblemCodes.ControllerUndeclaredAxes,
            $"The controller reports {reportedAxes} axes; index(es) {string.Join(", ", undeclared)} " +
            "are not declared by the profile. They are treated as unknown linear axes and are in no plane."));
    }

    /// <summary>Every axis in resolution order (planes first, then optional axes) with
    /// the plane it belongs to (-1 = optional), so a problem can point at its home.</summary>
    private static List<(MachineAxis Axis, int Plane)> CollectAxes(
        IReadOnlyList<ProfilePlane> planes, IReadOnlyList<MachineAxis> optionalAxes)
    {
        var all = new List<(MachineAxis, int)>();
        for (var p = 0; p < planes.Count; p++)
            foreach (var axis in planes[p].Axes)
                all.Add((axis, p));
        foreach (var axis in optionalAxes)
            all.Add((axis, -1));
        return all;
    }

    // Tolerate a hand-edited file: a null plane entry, a null axis entry, a null Axes
    // list. A null plane becomes an empty plane, which invariant 5 then reports —
    // silent data loss would be worse than a visible problem.
    private static List<ProfilePlane> NormalizePlanes(List<ProfilePlane> raw)
    {
        var planes = new List<ProfilePlane>(raw.Count);
        foreach (var plane in raw)
        {
            if (plane is null)
            {
                planes.Add(new ProfilePlane());
                continue;
            }
            planes.Add(plane.Axes.Any(a => a is null)
                ? new ProfilePlane { Axes = plane.Axes.Where(a => a is not null).ToList() }
                : plane);
        }
        return planes;
    }

    // The resolved object is a snapshot: the profile stays editable (and the provider
    // re-resolves on every trigger) without mutating what consumers already hold.
    private static MachineAxis Copy(MachineAxis axis) => new()
    {
        Index = axis.Index,
        Kind = axis.Kind,
        Direction = axis.Direction,
        Sign = axis.Sign,
        RotationOrigin = axis.RotationOrigin is null ? null : (double[])axis.RotationOrigin.Clone(),
        Alias = axis.Alias,
    };

    private static string Describe(IEnumerable<AxisDirection> directions) =>
        string.Join("/", directions);

    private static KinematicsProblem Error(string code, string message, int plane = -1, int axisIndex = -1) =>
        new(KinematicsSeverity.Error, code, message, plane, axisIndex);

    private static KinematicsProblem Warning(string code, string message, int plane = -1, int axisIndex = -1) =>
        new(KinematicsSeverity.Warning, code, message, plane, axisIndex);
}
