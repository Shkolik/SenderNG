
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

namespace SenderNG.Core.Modules.Probing;

/// <summary>Probing tools; values mirror ioSender's <c>ProbingType</c>.</summary>
public enum ProbingType
{
    None = 0,
    ToolLength,
    EdgeFinderInternal,
    EdgeFinderExternal,
    CenterFinder,
    Rotation,
    HeightMap,
}

/// <summary>Edge/corner selector (topology diagram, as in ioSender):</summary>
/// <code>
/// D |-----| C
///   |  Z  |
/// A |-----| B
/// </code>
public enum Edge
{
    None = 0,
    A,
    B,
    C,
    D,
    Z,
    AB,
    AD,
    CB,
    CD,
}

/// <summary>Center finder mode. <see cref="Inside"/>/<see cref="Outside"/> probe both
/// axes (legacy, ioSender's FindMode); the X/Y variants probe a single axis only.</summary>
public enum Center
{
    None = 0,
    Inside,
    Outside,

    /// <summary>Probe the pocket walls in X only (probe inside a pocket/hole).</summary>
    XInside,
    /// <summary>Probe the workpiece outside in X only (left and right sides).</summary>
    XOutside,
    /// <summary>Probe the pocket walls in Y only.</summary>
    YInside,
    /// <summary>Probe the workpiece outside in Y only (front and back sides).</summary>
    YOutside,
}

/// <summary>How the probed position is written to the controller.</summary>
public enum CoordinateMode
{
    /// <summary>Write the work coordinate system via G10 L2 Pn (or G10 L20 when relative).</summary>
    G10 = 0,
    /// <summary>Set the current position via G92.</summary>
    G92,
    /// <summary>Do not touch the controller; report the measured position only.</summary>
    Measure,
}

/// <summary>Work coordinate system selector, 1..6 → G54..G59 (ioSender's CsSelectControl).</summary>
public enum CoordinateSystem
{
    G54 = 1,
    G55 = 2,
    G56 = 3,
    G57 = 4,
    G58 = 5,
    G59 = 6,
}

/// <summary>
/// Rotation-apply pivot, ioSender's OriginControl surface: program-limits
/// corners/edges/center, the current DRO position, or (0, 0). Coordinates
/// resolve at apply time from the loaded program's limits.
/// </summary>
public enum RotationOrigin
{
    /// <summary>(0, 0).</summary>
    None = 0,
    /// <summary>Program-limits corner A (min X, min Y) — bottom-left in the
    /// probing diagram (A bottom-left, B bottom-right, C top-right,
    /// D top-left).</summary>
    A,
    /// <summary>Program-limits corner B (max X, min Y) — bottom-right.</summary>
    B,
    /// <summary>Program-limits corner C (max X, max Y) — top-right.</summary>
    C,
    /// <summary>Program-limits corner D (min X, max Y) — top-left.</summary>
    D,
    /// <summary>Program-limits center.</summary>
    Center,
    /// <summary>Bottom-edge midpoint (center X, min Y).</summary>
    AB,
    /// <summary>Left-edge midpoint (min X, center Y).</summary>
    AD,
    /// <summary>Right-edge midpoint (max X, center Y).</summary>
    CB,
    /// <summary>Top-edge midpoint (center X, max Y).</summary>
    CD,
    /// <summary>The current DRO (work) position.</summary>
    CurrentPos,
}
