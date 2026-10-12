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
(INCLUDING, NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Collections.Concurrent;

namespace CNC.Core.Services;

/// <summary>
/// Role of one axis inside its carriage plane. Roles are <b>positional</b>: a
/// machine type is an ordered list of carriage planes, each plane an ordered
/// list of roles, and the flattened position is the grbl axis index (grbl's
/// internal axis order is fixed — index 0 = first axis — and grblHAL letter
/// remaps never reorder axes). No letter→role dictionaries exist anywhere.
/// </summary>
public enum AxisRoleKind
{
    /// <summary>In-plane horizontal axis of the carriage plane (the plotter's X per layer).</summary>
    Horizontal,

    /// <summary>In-plane vertical axis of the carriage plane (the plotter's Y per layer).</summary>
    Vertical,

    /// <summary>Axis perpendicular to the plane (the mill's depth; the plotter's vertical).
    /// Multi-plane machines (foam) have none — the two towers are planes, not depths.</summary>
    Depth,
}

/// <summary>
/// One ordered carriage plane: its roles map positionally to consecutive axis
/// indices (e.g. <c>(Horizontal, Vertical)</c> starting at index 2 = axes 2,3
/// = a foam machine's second tower).
/// </summary>
/// <param name="Roles">Ordered role list; order is the axis-index order.</param>
public sealed record CarriagePlane(IReadOnlyList<AxisRoleKind> Roles)
{
    /// <summary>True when the plane carries a perpendicular (depth) axis.</summary>
    public bool HasDepth => Roles.Contains(AxisRoleKind.Depth);
}

/// <summary>
/// Immutable machine-type preset: the structural geometry model of a machine
/// (one plane + depth = mill; two coupled planes = foam). The preset owns
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

    /// <summary>Ordered carriage planes; flattened role positions are axis indices.</summary>
    public IReadOnlyList<CarriagePlane> Planes { get; init; } = [];

    /// <summary>Axis count the demo controller defaults to for new profiles of this type
    /// (seeded via the profile-creation hook; e.g. Foam → 4 so both towers exist).</summary>
    public int DefaultDemoAxes { get; init; } = 3;

    /// <summary>Conventional grbl axis order — the fallback letter set when the
    /// controller did not report <c>[AXS:]</c> letters.</summary>
    public const string ConventionalLetters = "XYZABCUVW";

    /// <summary>True when the machine has more than one carriage plane (drives
    /// jog's second cross + Together coupling; the foam flag without the string).</summary>
    public bool IsMultiPlane => Planes.Count > 1;

    /// <summary>Total axes the preset's planes project onto.</summary>
    public int AxisCount => Planes.Sum(p => p.Roles.Count);

    /// <summary>Conventional letter for a flattened axis index — the fallback
    /// display when the controller's handshake did not remap letters.
    /// Returns <c>'?'</c> beyond the conventional order (never expected; grbl
    /// supports 9 axes).</summary>
    public char ConventionalLetter(int axisIndex) =>
        axisIndex >= 0 && axisIndex < ConventionalLetters.Length
            ? ConventionalLetters[axisIndex]
            : '?';

    /// <summary>Axis indices (flattened role positions) backing carriage plane
    /// <paramref name="plane"/> — e.g. foam plane 1 → [2, 3]. Consumers slice
    /// per-axis data (toolpath segments, limits rows) with these instead of
    /// keeping any letter→index dictionaries.</summary>
    public int[] PlaneAxisIndices(int plane)
    {
        if (plane < 0 || plane >= Planes.Count)
            throw new ArgumentOutOfRangeException(nameof(plane));
        var offset = 0;
        for (var p = 0; p < plane; p++)
            offset += Planes[p].Roles.Count;
        return Enumerable.Range(offset, Planes[plane].Roles.Count).ToArray();
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
            // One carriage plane (X,Y) plus the depth axis (Z) — the plotter's
            // XY flat + isometric views, DRO work zero on the depth axis.
            Planes = [new CarriagePlane([AxisRoleKind.Horizontal, AxisRoleKind.Vertical, AxisRoleKind.Depth])],
            DefaultDemoAxes = 3,
        });
        Register(new MachineType
        {
            Id = MachineTypes.Foam,
            Name = "Foam",
            // Two coupled carriage planes: tower 1 (X,Y) and tower 2 (Z,A).
            // Jog: two crosses + Together; plotter: isometric loft | XY/ZA flats.
            Planes =
            [
                new CarriagePlane([AxisRoleKind.Horizontal, AxisRoleKind.Vertical]),
                new CarriagePlane([AxisRoleKind.Horizontal, AxisRoleKind.Vertical]),
            ],
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
