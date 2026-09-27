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

namespace SenderNG.Modules.Plotter.Settings;

/// <summary>Pure plane→world mapping. Nothing here touches Avalonia or the
/// renderer: the flat panes, the iso panes, the header letters and the chrome
/// painters all read the SAME mapping through these functions, so a plane's
/// bounds, path, glyph and letters can never disagree.</summary>
public static class PlotterPlaneMapper
{
    /// <summary>World axes are X, Y, Z.</summary>
    public const int WorldAxisCount = 3;

    /// <summary>Identity mapping for a plane with <paramref name="roles"/>
    /// machine axes: slot <c>i</c> feeds world axis <c>i</c>.</summary>
    public static PlotterPlaneGeometry Identity(int roles) => new()
    {
        WorldAxes = Enumerable.Range(0, roles).ToArray(),
        Origin = [0, 0, 0],
    };

    /// <summary>True when <paramref name="g"/> is a well-formed mapping for a
    /// plane with <paramref name="roles"/> machine axes: right length, no
    /// repeats, every world axis in range.</summary>
    public static bool IsUsable(PlotterPlaneGeometry? g, int roles) =>
        g?.WorldAxes is { } axes
        && axes.Length == roles
        && axes.Distinct().Count() == roles
        && axes.All(w => w >= 0 && w < WorldAxisCount);

    /// <summary><paramref name="g"/> when it is usable, otherwise
    /// <see cref="Identity"/> for <paramref name="roles"/>.</summary>
    public static PlotterPlaneGeometry For(PlotterPlaneGeometry? g, int roles) =>
        IsUsable(g, roles) ? g! : Identity(roles);

    /// <summary>Which plane slot feeds <paramref name="worldAxis"/>, or −1
    /// when the plane does not map it.</summary>
    public static int SlotForWorldAxis(int[]? worldAxes, int worldAxis)
    {
        if (worldAxes is null)
            return -1;
        for (var slot = 0; slot < worldAxes.Length; slot++)
        {
            if (worldAxes[slot] == worldAxis)
                return slot;
        }
        return -1;
    }

    /// <summary>The plane slots that feed world X and world Y, in that order.
    /// Either may be −1 — a plane feeding only X and Z has no world Y, and the
    /// flat 2D pane must then refuse to draw it.</summary>
    public static (int X, int Y) Feeds(PlotterPlaneGeometry geometry)
    {
        var axes = geometry.WorldAxes;
        return (SlotForWorldAxis(axes, 0), SlotForWorldAxis(axes, 1));
    }

    /// <summary>The plane's world-axis offset. 0 when the plane has no origin
    /// array, when the array is the wrong length, or when the axis is out of
    /// range — a settings file is untrusted input, so this is a clamp, not an
    /// index.</summary>
    public static double OriginFor(PlotterPlaneGeometry? g, int worldAxis) =>
        g?.Origin is { Length: WorldAxisCount } origin
        && (uint)worldAxis < WorldAxisCount
            ? origin[worldAxis]
            : 0;
}
