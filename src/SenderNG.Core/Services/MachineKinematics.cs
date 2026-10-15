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

/// <summary>How serious a <see cref="KinematicsProblem"/> is. Errors mean the profile
/// cannot be honoured as written and the editor must offer a fix; warnings are
/// tolerated by consumers but worth showing (superset tolerance, a dead value).</summary>
public enum KinematicsSeverity
{
    Warning,
    Error,
}

/// <summary>
/// Stable problem codes. These are the contract the profile editor localizes on —
/// <see cref="KinematicsProblem.Message"/> is the developer-facing English text and
/// must not be parsed.
/// </summary>
public static class KinematicsProblemCodes
{
    /// <summary>Invariant 1: an axis index outside 0..8.</summary>
    public const string AxisIndexRange = "axis.indexRange";
    /// <summary>Invariant 1: the same index used by two axes (planes + optional axes).</summary>
    public const string AxisIndexDuplicate = "axis.indexDuplicate";
    /// <summary>Invariant 9: <c>Sign</c> is neither +1 nor -1.</summary>
    public const string AxisSign = "axis.sign";
    /// <summary>Invariant 2: a plane holds more than three axes.</summary>
    public const string PlaneTooManyAxes = "plane.tooManyAxes";
    /// <summary>Invariant 3: a rotary axis sits inside a plane.</summary>
    public const string PlaneRotaryAxis = "plane.rotaryAxis";
    /// <summary>Invariant 4: multi-plane machines' planes do not share their in-plane pair.</summary>
    public const string PlaneDirectionPair = "plane.directionPair";
    /// <summary>Invariant 5: the plane list or a plane's axis count differs from the preset.</summary>
    public const string PlaneCountMismatch = "plane.countMismatch";
    /// <summary>Invariant 6: a single-plane machine carries a non-zero separation.</summary>
    public const string SeparationUnexpected = "separation.unexpected";
    /// <summary>Invariant 7: a rotary axis without exactly two pivot coordinates.</summary>
    public const string RotaryOriginArity = "rotary.originArity";
    /// <summary>Invariant 7: a linear axis carrying a rotation origin.</summary>
    public const string LinearOriginPresent = "linear.originPresent";
    /// <summary>Invariant 8: a CAM alias that the board's letters already use.</summary>
    public const string AliasBoardLetter = "alias.boardLetter";
    /// <summary>Invariant 8: the same CAM alias on two axes.</summary>
    public const string AliasDuplicate = "alias.duplicate";
    /// <summary>Invariant 10: the controller reports axes the profile does not declare.</summary>
    public const string ControllerUndeclaredAxes = "controller.undeclaredAxes";
}

/// <summary>
/// One validation problem found while resolving a profile. Returned as <b>data</b>,
/// never thrown: the profile editor renders the list and a hand-edited profile file
/// must not crash the app.
/// </summary>
/// <param name="Severity">Error (unusable as written) or warning (tolerated).</param>
/// <param name="Code">One of <see cref="KinematicsProblemCodes"/> — the localizable key.</param>
/// <param name="Message">English developer/UI text; never parse it, key off <paramref name="Code"/>.</param>
/// <param name="Plane">Plane the problem belongs to, or -1 when it is not plane-specific.</param>
/// <param name="AxisIndex">Axis index the problem belongs to, or -1 when it is not axis-specific.</param>
public sealed record KinematicsProblem(
    KinematicsSeverity Severity,
    string Code,
    string Message,
    int Plane = -1,
    int AxisIndex = -1);

/// <summary>
/// One resolved plane: its axes in plane order (<b>not</b> index order — the bug-report
/// foam firmware's plane 2 is <c>[A, Z]</c> = indices <c>[3, 2]</c>) plus the
/// plane-to-plane separation it sits at.
/// </summary>
public sealed class KinematicPlane
{
    internal KinematicPlane(IReadOnlyList<MachineAxis> axes, double separation)
    {
        Axes = axes;
        Separation = separation;
    }

    /// <summary>Ordered axes: slot 0 is the plane's horizontal, slot 1 its vertical,
    /// optional slot 2 the perpendicular. Read-only snapshots of the profile's axes.</summary>
    public IReadOnlyList<MachineAxis> Axes { get; }

    /// <summary>Plane-to-plane distance in mm: <c>0</c> for plane 0 and for
    /// single-plane machines, the profile's <c>Separation</c> for later planes.</summary>
    public double Separation { get; }
}

/// <summary>
/// One machine's resolved kinematics: the machine-type preset ⊕ the active profile ⊕
/// the connected controller (letters from <c>[AXS:]</c> and the reported axis count).
/// Immutable snapshot — the single object the jog cross, plotter, outline, DRO and
/// limits read instead of each re-resolving the preset at their own trigger point.
/// </summary>
public sealed class MachineKinematics
{
    internal MachineKinematics(
        string profileId,
        string machineTypeId,
        IReadOnlyList<KinematicPlane> planes,
        IReadOnlyList<MachineAxis> optionalAxes,
        int declaredAxes,
        int reportedAxes,
        IReadOnlyList<int> rotaryAxes,
        string boardLetters,
        IReadOnlyList<KinematicsProblem> problems)
    {
        ProfileId = profileId;
        MachineTypeId = machineTypeId;
        Planes = planes;
        OptionalAxes = optionalAxes;
        DeclaredAxes = declaredAxes;
        ReportedAxes = reportedAxes;
        RotaryAxes = rotaryAxes;
        BoardLetters = boardLetters;
        Problems = problems;
    }

    /// <summary>The profile this resolution came from (diagnostics, and so a consumer
    /// can tell two machines apart even when their layouts are identical).</summary>
    public string ProfileId { get; }

    /// <summary>The preset id this resolution came from (diagnostics / consumer branching).</summary>
    public string MachineTypeId { get; }

    /// <summary>Resolved planes, in machine plane order, each with its separation.</summary>
    public IReadOnlyList<KinematicPlane> Planes { get; }

    /// <summary>Axes outside every plane; rotary axes always live here.</summary>
    public IReadOnlyList<MachineAxis> OptionalAxes { get; }

    /// <summary>Axes the profile declares (planes + optional axes).</summary>
    public int DeclaredAxes { get; }

    /// <summary>Axes the controller reports; <c>0</c> = none/disconnected.</summary>
    public int ReportedAxes { get; }

    /// <summary>More than one plane (foam); drives jog's second cross and Together.</summary>
    public bool IsMultiPlane => Planes.Count > 1;

    /// <summary>Indices of every rotary axis, ascending. Wrap/unwrap gates on this
    /// instead of on a raw axis index (foam's 4th axis is a linear tower axis).</summary>
    public IReadOnlyList<int> RotaryAxes { get; }

    /// <summary>The controller's <c>[AXS:]</c> letters, or the conventional
    /// <c>XYZABCUVW</c> when it reported none (disconnected / classic grbl).</summary>
    public string BoardLetters { get; }

    /// <summary>Validation problems, as data. Empty when the profile is clean.</summary>
    public IReadOnlyList<KinematicsProblem> Problems { get; }

    /// <summary>True when no problem is an <see cref="KinematicsSeverity.Error"/>.</summary>
    public bool IsValid => !Problems.Any(p => p.Severity == KinematicsSeverity.Error);

    /// <summary>Every error, without the warnings.</summary>
    public IEnumerable<KinematicsProblem> Errors =>
        Problems.Where(p => p.Severity == KinematicsSeverity.Error);

    /// <summary>
    /// The letter to show for an axis index: the controller's board letter at that
    /// index, else the conventional fallback when the board reported none. <c>'?'</c>
    /// for an index past the known letters (the letter is never stored, so it can
    /// never disagree with the board).
    /// </summary>
    public char LetterFor(int index) =>
        index >= 0 && index < BoardLetters.Length ? BoardLetters[index] : '?';

    /// <summary>The axis index in a plane's slot, or <c>-1</c> when the slot (or the
    /// plane) does not exist. Slots are role positions, so this is what replaces
    /// <c>LetterFor(0..3)</c> / <c>indices[^1]</c> assumptions.</summary>
    public int IndexAt(int plane, int slot)
    {
        if (plane < 0 || plane >= Planes.Count)
            return -1;
        var axes = Planes[plane].Axes;
        return slot >= 0 && slot < axes.Count ? axes[slot].Index : -1;
    }

    /// <summary>The index of the axis lying along <paramref name="direction"/> inside a
    /// plane, or <c>-1</c> when that plane has no axis on it (foam planes have none on
    /// the separation direction).</summary>
    public int IndexForDirection(int plane, AxisDirection direction)
    {
        if (plane < 0 || plane >= Planes.Count)
            return -1;
        foreach (var axis in Planes[plane].Axes)
            if (axis.Direction == direction)
                return axis.Index;
        return -1;
    }

    /// <summary>
    /// The world axis a plane's slot feeds. Within a plane slot k feeds world axis k —
    /// there is no per-plane mapping table to store — so this is the slot itself, or
    /// <c>-1</c> when the slot does not exist.
    /// </summary>
    public int WorldAxisFor(int plane, int slot) => IndexAt(plane, slot) < 0 ? -1 : slot;

    /// <summary>Plane-to-plane separation: <c>0</c> for plane 0, a single-plane machine
    /// and an invalid plane; the profile's separation for a later plane.</summary>
    public double SeparationFor(int plane) =>
        plane >= 0 && plane < Planes.Count ? Planes[plane].Separation : 0;

    /// <summary>True when the controller reports this axis. Declared axes past
    /// <see cref="ReportedAxes"/> are simply not present (invariant 10).</summary>
    public bool IsPresent(int index) => index >= 0 && index < ReportedAxes;

    /// <summary>
    /// A plane's axis indices in <b>plane order</b> — deliberately not sorted, because
    /// the order carries the plane's orientation (foam plane 2 = <c>[3, 2]</c>). Empty
    /// for a plane that does not exist.
    /// </summary>
    public IReadOnlyList<int> PlaneIndices(int plane) =>
        plane >= 0 && plane < Planes.Count
            ? Planes[plane].Axes.Select(a => a.Index).ToArray()
            : [];
}
