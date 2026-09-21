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

namespace SenderNG.Modules.Plotter.Nex.Settings;

/// <summary>Per-profile Plot 3D rendering config (<c>MachineProfile.ModuleSettings["plotter-nex"]</c>).</summary>
public sealed class PlotterNexSettings
{
    /// <summary>Distance between the towers (machine units). Changing it recomputes
    /// the plane origins along the wire axis (symmetric centering).</summary>
    public double WireLength { get; set; } = 100;

    /// <summary>Show the origin pivot + chrome.</summary>
    public bool ShowOrigin { get; set; } = true;

    /// <summary>Show the combined work/job envelope wireframe.</summary>
    public bool ShowEnvelope { get; set; } = true;

    /// <summary>Show run-state coloring + executed/run-state overlay.</summary>
    public bool ShowRunState { get; set; } = true;

    /// <summary>Show the grid plane.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>AA method (int ordinal): 0=Off, 1=SMAA, 2=FXAA. Default SMAA =
    /// current behavior.</summary>
    public int AaMode { get; set; } = 1;

    /// <summary>AA quality (int ordinal, shared ladder): 0=Low, 1=Medium,
    /// 2=High, 3=Ultra. Default High = current behavior.</summary>
    public int AaQuality { get; set; } = 2;

    /// <summary>Render the readback target at 2x panel size and let the control
    /// downsample on display, giving hairline lines a 2px SMAA footprint. Default
    /// ON; disable on weaker machines (4x the raster + readback cost).</summary>
    public bool Supersample { get; set; } = true;

    /// <summary>Ordered per-machine plane; <c>WorldAxes[s]</c> = world axis index
    /// (0=X, 1=Y, 2=Z) for the plane's slot <c>s</c> (distinct); <c>Origin</c> =
    /// world position of the plane's origin. Length must equal the schema plane's
    /// role count / 3 respectively (enforced by <see cref="PlotterNexConfig"/>).</summary>
    public List<NexPlaneGeometry> Planes { get; set; } = [];
}

/// <summary>One machine carriage plane's world mapping.</summary>
public sealed class NexPlaneGeometry
{
    /// <summary>World axis index per slot; distinct; length == the plane's <c>Roles.Count</c>.</summary>
    public int[] WorldAxes { get; set; } = [];

    /// <summary>World position of the plane's origin (length 3: X, Y, Z).</summary>
    public double[] Origin { get; set; } = [0, 0, 0];
}