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

using SenderNG.Modules.Common.Render;

namespace SenderNG.Modules.Plotter.GL.Settings;

/// <summary>Per-profile Plot GL rendering config (<c>MachineProfile.ModuleSettings["plotter-gl"]</c>).
/// <para>The per-plane world mapping and the wire-length scalar this section used
/// to carry are <b>deleted</b>: the plane layout (count, slices and the
/// plane-to-plane separation) comes from <c>IMachineKinematicsProvider</c>, and
/// the profile's own <c>Separation</c> is the machine-model home for the tower
/// distance. What is left is purely presentational.</para>
/// <para>A stale section still carrying <c>Planes</c>/<c>WireLength</c>
/// deserializes with those members ignored (<see cref="PlotterGlConfig"/>).</para></summary>
public sealed class PlotterGlSettings
{
    /// <summary>Show the origin pivot + chrome.</summary>
    public bool ShowOrigin { get; set; } = true;

    /// <summary>Show the translucent loft sheet between the carriage planes
    /// (multi-plane machines only — the toggle is gated by
    /// <c>HasLoft</c>).</summary>
    public bool ShowLoft { get; set; } = true;

    /// <summary>Show the combined work/job envelope wireframe.</summary>
    public bool ShowEnvelope { get; set; } = true;

    /// <summary>Show run-state coloring + executed/run-state overlay.</summary>
    public bool ShowRunState { get; set; } = true;

    /// <summary>Show the grid plane.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>Render the readback target at 2x panel size and let the control
    /// downsample on display, giving hairline lines a 2px footprint. Default
    /// ON; disable on weaker machines (4x the raster + readback cost).</summary>
    public bool Supersample { get; set; } = true;

    /// <summary>Multisample count of the readback target (None / 2x / 4x / 8x) —
    /// the orthogonal axis to <see cref="Supersample"/> (sample count vs
    /// render size, they compose). Default None until checkpoint verification
    /// proves a win on the target machines.</summary>
    public MsaaMode MsaaMode { get; set; } = MsaaMode.None;
}
