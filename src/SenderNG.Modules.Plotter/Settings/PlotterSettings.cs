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

/// <summary>The <c>plotter</c> section: the four view toggles, nothing else.
/// <para>The per-plane axis→world mapping this section used to carry is
/// <b>deleted</b>, not moved: the machine kinematics owns the axis layout, and
/// within a plane slot <c>k</c> feeds world axis <c>k</c> by construction, so
/// there is no mapping left to store. A stale section still carrying
/// <c>Planes</c> deserializes with those members ignored
/// (<see cref="PlotterConfig"/>).</para>
/// <para>All toggles default <c>true</c>, so a profile that has never seen this
/// section renders exactly as it did before.</para></summary>
public sealed class PlotterSettings
{
    /// <summary>World XY grid, on the iso floor and face-on in the flat panes.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>The world axis arrows, which leave the machine origin along
    /// each axis. This is what marks the origin: there is no separate
    /// crosshair, because a second mark on the same corner said the same
    /// thing twice. Same ShowOrigin → SetAxesVisible mapping.</summary>
    public bool ShowOrigin { get; set; } = true;

    /// <summary>The job's rectangle in the flat panes / box in iso.</summary>
    public bool ShowEnvelope { get; set; } = true;

    /// <summary>Live run-state coloring. Off resolves every row to idle, which
    /// the bake already falls through to the neutral feed pen.</summary>
    public bool ShowRunState { get; set; } = true;
}
