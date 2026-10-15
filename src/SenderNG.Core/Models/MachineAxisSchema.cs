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

using System.Collections.Concurrent;

namespace CNC.Core.Services;

/// <summary>
/// Physical kind of an axis. A rotary axis is <b>not</b> a position in a plane's
/// 2D frame, so it never lives inside a <see cref="ProfilePlane"/> — it is an
/// optional axis (invariant 3).
/// </summary>
public enum AxisKind
{
    /// <summary>Slides along its <see cref="MachineAxis.Direction"/> line.</summary>
    Linear,

    /// <summary>Spins about its <see cref="MachineAxis.Direction"/> line; the pivot is
    /// <see cref="MachineAxis.RotationOrigin"/>.</summary>
    Rotary,
}

/// <summary>
/// The direction an axis IS, in the machine's own frame. A linear axis slides
/// along it; a rotary axis spins <b>about</b> it — so the direction names the
/// line, and the pivot is stored separately.
/// </summary>
public enum AxisDirection
{
    /// <summary>Across the machine — the direction a hot-wire foam machine's two towers
    /// are separated along (never spanned by one of their axes).</summary>
    Width,

    /// <summary>Into the machine (the longer table direction).</summary>
    Length,

    /// <summary>Vertical.</summary>
    Height,
}

/// <summary>
/// One axis of a machine profile. <see cref="Index"/> (the grbl axis index) is the
/// single stored truth: the <b>letter</b> shown for it is derived from the
/// controller's <c>[AXS:]</c> letters at connect time, never persisted, so the
/// sender can never disagree with the board about which motor moves.
/// </summary>
public sealed class MachineAxis
{
    /// <summary>grbl axis index 0..8 — the truth.</summary>
    public int Index { get; set; }

    /// <summary>Linear or rotary (invariant 3: rotary never sits in a plane).</summary>
    public AxisKind Kind { get; set; }

    /// <summary>The machine-frame direction this axis lies along.</summary>
    public AxisDirection Direction { get; set; }

    /// <summary>Direction sign: <c>+1</c> or <c>-1</c> (invariant 9).</summary>
    public int Sign { get; set; } = 1;

    /// <summary>
    /// Rotary only: the pivot in MACHINE coordinates, as exactly the <b>two</b>
    /// coordinates perpendicular to <see cref="Direction"/> (the remaining
    /// directions in canonical Width, Length, Height order). The component along
    /// the rotation direction is free — every point on that line is the same line
    /// — so it is never stored. Linear axes must not carry one (invariant 7).
    /// Stored in machine coordinates: a consumer drawing in work coordinates must
    /// subtract the active work offset.
    /// </summary>
    public double[]? RotationOrigin { get; set; }

    /// <summary>
    /// Optional CAM alias: a letter a post-processor writes for this index when the
    /// board names it differently. <b>INBOUND ONLY</b> — never displayed, and it
    /// never displaces <see cref="Index"/> as the stored truth. A letter is
    /// resolved through the board's letters first, then these aliases (invariant 8).
    /// </summary>
    public char? Alias { get; set; }
}

/// <summary>
/// One plane of a machine profile. Ordered: slot 0 = the plane's horizontal,
/// slot 1 = its vertical, optional slot 2 = the perpendicular (the mill's depth).
/// <para>
/// The list order <b>is</b> the plane's orientation — it is not a restatement of
/// index order — so a plane's indices may be non-monotonic: the bug-report foam
/// firmware's plane 2 is <c>[index 3 (A), index 2 (Z)]</c>. Consumers must use
/// <c>MachineKinematics.IndexAt</c>/<c>PlaneIndices</c>, never <c>indices[^1]</c>.
/// </para>
/// </summary>
public sealed class ProfilePlane
{
    private List<MachineAxis> _axes = [];

    /// <summary>Ordered axes; never null (a hand-edited file carrying
    /// <c>"Axes": null</c> reads as an empty plane, which the resolver reports).</summary>
    public List<MachineAxis> Axes
    {
        get => _axes;
        set => _axes = value ?? [];
    }
}

/// <summary>
/// Immutable machine-type preset: the structural geometry model of a machine
/// (one base plane + depth = mill; two coupled planes = foam). The preset owns
/// <b>structure, not identity</b> — which physical letters ride those roles is
/// the controller's business (<c>GrblInfo.AxisLetters</c> from the handshake;
/// the conventional letters here are only the fallback display).
/// </summary>
public sealed class MachineType
{
    /// <summary>Preset id referenced by <c>MachineProfile.MachineTypeId</c>
    /// (built-ins: <see cref="MachineTypes"/>; plugin ids later).</summary>
    public required string Id { get; init; }

    /// <summary>Display name (profile manager badge; localization happens in the UI layer).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Base topology: one ordered direction list per plane. The preset fixes the
    /// plane count and each plane's axis count; a profile binds concrete indices
    /// and roles to it, seeded on create from these defaults.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<AxisDirection>> BasePlanes { get; init; } = [];

    /// <summary>
    /// The direction no axis of this type spans — the single degree of freedom two
    /// parallel planes leave. Foam: <see cref="AxisDirection.Width"/> (the wire
    /// length). Null for single-plane machines.
    /// </summary>
    public AxisDirection? SeparationDirection { get; init; }

    /// <summary>Plane-to-plane distance (mm) seeded into a fresh profile's
    /// <c>MachineProfile.Separation</c>. Only a multi-plane machine consumes it;
    /// Foam ships 200 mm so its two towers do not start coincident.</summary>
    public double DefaultSeparation { get; init; }

    /// <summary>Axis count the demo controller defaults to for new profiles of this type
    /// (seeded via the profile-creation hook; e.g. Foam → 4 so both towers exist).</summary>
    public int DefaultDemoAxes { get; init; } = 3;

    /// <summary>Conventional grbl axis order — the fallback letter set when the
    /// controller did not report <c>[AXS:]</c> letters.</summary>
    public const string ConventionalLetters = "XYZABCUVW";

    /// <summary>
    /// The profile planes a fresh profile of this type starts from: the preset's
    /// base structure bound to the default conventional indices (the flattened
    /// position), all linear, sign <c>+1</c>. The role <b>bindings</b> are the
    /// user's afterwards; the axis and plane counts are locked by the preset.
    /// </summary>
    public List<ProfilePlane> CreateDefaultPlanes()
    {
        var planes = new List<ProfilePlane>();
        var index = 0;
        foreach (var basePlane in BasePlanes)
        {
            var plane = new ProfilePlane();
            foreach (var direction in basePlane)
            {
                plane.Axes.Add(new MachineAxis
                {
                    Index = index++,
                    Kind = AxisKind.Linear,
                    Direction = direction,
                    Sign = 1,
                });
            }
            planes.Add(plane);
        }
        return planes;
    }
}

/// <summary>
/// Registry of machine-type presets. Built-ins (<see cref="MachineTypes"/>) are
/// registered in the static constructor; plugins may register their own types
/// (a later module-contract hook — the API exists here from day one). Unknown
/// ids resolve to Mill + the caller surfaces a warning (a removed plugin
/// type must not brick a profile).
/// </summary>
public static class MachineTypeCatalog
{
    // Concurrent: Resolve is called from UI/service code while a plugin (or a
    // test) may Register concurrently — reads must stay safe against writes.
    private static readonly ConcurrentDictionary<string, MachineType> Types = new(StringComparer.Ordinal);

    static MachineTypeCatalog()
    {
        Register(new MachineType
        {
            Id = MachineTypes.Mill,
            Name = "Mill",
            // One base plane: two in-plane axes plus the perpendicular depth —
            // the plotter's XY flat + isometric views, DRO work zero on the depth.
            BasePlanes = [[AxisDirection.Width, AxisDirection.Length, AxisDirection.Height]],
            DefaultDemoAxes = 3,
        });
        Register(new MachineType
        {
            Id = MachineTypes.Foam,
            Name = "Foam",
            // Two coupled planes: tower 1 and tower 2, each moving in the
            // Length/Height plane and separated along Width — the direction no
            // axis of either tower spans, which is exactly the wire.
            // Jog: two crosses + Together; plotter: isometric loft | XY/ZA flats.
            BasePlanes =
            [
                [AxisDirection.Length, AxisDirection.Height],
                [AxisDirection.Length, AxisDirection.Height],
            ],
            SeparationDirection = AxisDirection.Width,
            // A sane out-of-the-box tower separation: a fresh foam profile must not
            // render the two towers coincident (0 is physically impossible for a
            // hot-wire machine), and the GL loft's own historical default was 100.
            // The machine card's plane-to-plane field overwrites it per profile.
            DefaultSeparation = 200,
            DefaultDemoAxes = 4,
        });
    }

    /// <summary>All registered types (built-ins + plugin registrations), in registration order.</summary>
    public static IEnumerable<MachineType> All => Types.Values;
    /// <summary>Register a machine type. Throws on a null/empty id or a duplicate
    /// id — duplicate registration is a plugin bug, not a replace operation.</summary>
    public static void Register(MachineType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (string.IsNullOrWhiteSpace(type.Id))
            throw new ArgumentException("Machine type id must not be empty.", nameof(type));
        if (!Types.TryAdd(type.Id, type))
            throw new ArgumentException($"Machine type '{type.Id}' is already registered.", nameof(type));
    }

    /// <summary>Resolve a profile's machine type id. Null/empty/unknown ids fall
    /// back to the built-in Mill preset (never null); callers surface a warning
    /// when the id was set but unknown (removed plugin type).</summary>
    public static MachineType Resolve(string? typeId)
    {
        if (!string.IsNullOrEmpty(typeId) && Types.TryGetValue(typeId, out var type))
            return type;
        return Types[MachineTypes.Mill];
    }
}
