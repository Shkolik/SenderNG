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

using System.Globalization;

namespace CNC.Core.Services;

/// <summary>How serious a <see cref="KinematicsProblem"/> is. Errors mean the profile
/// cannot be honoured as written; warnings are tolerated by consumers (superset
/// tolerance, a dead value).</summary>
public enum KinematicsSeverity
{
    Warning,
    Error,
}

/// <summary>
/// Stable problem codes. Consumers key off these, not off the message;
/// <see cref="KinematicsProblem.Message"/> is developer-facing English text and must
/// not be parsed. <see cref="KinematicsProblemText"/> localizes the one code the UI
/// surfaces (the DRO's undeclared-axes warning).
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
/// never thrown: consumers decide what to surface and a hand-edited profile file must
/// not crash the app.
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
/// The one place a <see cref="KinematicsProblem"/> becomes user-visible text. The
/// locale key is derived from <see cref="KinematicsProblem.Code"/> — never by parsing
/// <see cref="KinematicsProblem.Message"/>, which is developer-facing English — and
/// that message is only the fallback for an absent locale service or a catalog missing
/// the key, so a missing translation can never surface a raw key.
/// <para>Used by the DRO's one-time undeclared-axes warning, so the code-to-sentence
/// mapping has a single home.</para>
/// </summary>
public static class KinematicsProblemText
{
    /// <summary>The <c>appSettings.axes.problem.*</c> catalog key for a problem. The
    /// plane-count mismatch carries two sentences — one plane's axis count vs the whole
    /// plane list — so a problem that names no specific plane is the whole-list one.</summary>
    public static string KeyFor(KinematicsProblem problem) =>
        problem.Code == KinematicsProblemCodes.PlaneCountMismatch && problem.Plane < 0
            ? "appSettings.axes.problem.planes.countMismatch"
            : "appSettings.axes.problem." + problem.Code;

    /// <summary>The value a sentence's <c>{0}</c> takes: the offending axis index, else
    /// the 1-based plane number, else 0 for a sentence with no placeholder.</summary>
    public static int PlaceholderFor(KinematicsProblem problem) =>
        problem.AxisIndex >= 0
            ? problem.AxisIndex
            : problem.Plane >= 0
                ? problem.Plane + 1
                : 0;

    /// <summary>
    /// Resolve a problem through <paramref name="resolve"/> and substitute the
    /// sentence's <c>{0}</c>. <paramref name="resolve"/> is a <c>(key, English
    /// fallback) → text</c> lookup shaped like <c>LocalizationService.Resolve</c> and
    /// <c>LocText.Resolve</c>, so a caller passes whichever locale source it owns.
    /// </summary>
    public static string Localize(KinematicsProblem problem, Func<string, string, string> resolve) =>
        resolve(KeyFor(problem), problem.Message)
            .Replace("{0}", PlaceholderFor(problem).ToString(CultureInfo.CurrentCulture));
}

/// <summary>
/// One resolved plane: its axes in canonical plane order (<b>not</b> index order —
/// the bug-report foam firmware's plane 2 is <c>[A, Z]</c> = indices <c>[3, 2]</c>)
/// plus the plane-to-plane separation it sits at. The pair is canonical: when the
/// stored profile lists the Height axis first, the resolver swaps it into the
/// vertical slot, so slot 1 is the Height axis of any pair that spans one.
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
    private IReadOnlyDictionary<char, int>? aliasMap;

    /// <summary>
    /// The profile's CAM letter aliases as the letter → axis-index map a program
    /// parser takes (<c>AxisLetters.TryResolve</c>'s alias argument): every
    /// declared axis's <see cref="MachineAxis.Alias"/>, planes and optional axes
    /// alike. Empty when the user declared none.
    /// <para>The resolver <i>flags</i> an alias that collides with the board's
    /// letters or with another alias (<see cref="KinematicsProblemCodes.AliasBoardLetter"/>,
    /// <see cref="KinematicsProblemCodes.AliasDuplicate"/>, invariant 8) but does not
    /// remove it, so this map can carry a flagged alias. Duplicates are written
    /// last-wins in plane-then-optional order, which keeps the resolution
    /// deterministic. Inbound only — never displayed.</para>
    /// <para><b>Profile state, deliberately not connection state.</b> The map is
    /// derived from the profile alone, so a disconnected machine still reports it and
    /// a program parsed while disconnected resolves its CAM letters. Do not "fix" it
    /// to empty on disconnect: parsing happens at load time (the load gate, the
    /// transformers), which can precede the connection, and an emptied map would
    /// silently re-seat an alias letter on its conventional index — the wrong physical
    /// axis on a token list that runs later. The provider re-derives and republishes it
    /// on every connect, disconnect and profile switch, so no value from a previous
    /// board or profile can survive in it.</para>
    /// </summary>
    public IReadOnlyDictionary<char, int> AliasMap => aliasMap ??= BuildAliasMap();

    private IReadOnlyDictionary<char, int> BuildAliasMap()
    {
        var map = new Dictionary<char, int>();
        foreach (var axis in Planes.SelectMany(p => p.Axes).Concat(OptionalAxes))
        {
            if (axis.Alias is not { } alias)
                continue;
            // Both cases: the resolver normalises the letter it is handed, and
            // which side it normalises is an implementation detail this map must
            // not depend on. A lowercase alias stores the same value twice.
            map[alias] = axis.Index;
            map[char.ToUpperInvariant(alias)] = axis.Index;
        }
        return map;
    }

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
    /// never disagree with the board). Same helper the g-code token renderers use.
    /// </summary>
    public char LetterFor(int index) => AxisLetters.LetterAt(BoardLetters, index);

    /// <summary>The axis index in a plane's slot, or <c>-1</c> when the slot (or the
    /// plane) does not exist. Slots are role positions in the resolver's canonical
    /// cross order (a pair's Height axis is always the vertical slot,
    /// see <c>docs/plans/2026-10-17-cross-order-normalization.md</c>), so this is what
    /// replaces <c>LetterFor(0..3)</c> / <c>indices[^1]</c> assumptions.</summary>
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
    /// The world axis a machine <paramref name="direction"/> feeds in the 3D views:
    /// Width→world X, Length→world Y, <b>Height→world Z</b>. Height is always world
    /// Z because a machine's height is vertical by definition — that is what puts a
    /// foam tower (Length × Height) on a <i>vertical</i> plane and leaves its
    /// separation on the free horizontal axis, instead of stacking the two towers
    /// like horizontal shelves.
    /// <para>It also leaves the mill untouched: its one plane spans Width, Length
    /// and Height in that order, so it still maps to X, Y, Z.</para>
    /// </summary>
    public static int WorldAxisForDirection(AxisDirection direction) => direction switch
    {
        AxisDirection.Width => 0,
        AxisDirection.Length => 1,
        _ => 2,
    };

    /// <summary>
    /// The world axis a plane's slot feeds, read from that axis's direction (see
    /// <see cref="WorldAxisForDirection"/>); <c>-1</c> when the slot does not exist.
    /// </summary>
    public int WorldAxisFor(int plane, int slot)
    {
        if (plane < 0 || plane >= Planes.Count)
            return -1;
        var axes = Planes[plane].Axes;
        return slot >= 0 && slot < axes.Count ? WorldAxisForDirection(axes[slot].Direction) : -1;
    }

    /// <summary>
    /// A plane's world axes in slot order — what a 3D view needs to place one
    /// machine position: slot <c>k</c>'s value belongs on element <c>k</c>.
    /// </summary>
    public int[] WorldAxesFor(int plane)
    {
        if (plane < 0 || plane >= Planes.Count)
            return [];
        return [.. Planes[plane].Axes.Select(a => WorldAxisForDirection(a.Direction))];
    }

    /// <summary>
    /// The world axis this plane does <b>not</b> span — where a multi-plane
    /// machine's separation places it, one tower per side. <c>-1</c> when the plane
    /// spans all three (a mill), which is never displaced.
    /// </summary>
    public int FreeWorldAxisFor(int plane)
    {
        if (plane < 0 || plane >= Planes.Count)
            return -1;
        var spanned = 0;
        foreach (var axis in Planes[plane].Axes)
            spanned |= 1 << WorldAxisForDirection(axis.Direction);
        for (var world = 0; world < 3; world++)
            if ((spanned & (1 << world)) == 0)
                return world;
        return -1;
    }

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
