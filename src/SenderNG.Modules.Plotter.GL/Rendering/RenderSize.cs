/*
 * RenderSize.cs - panel-vs-render viewport size decoupling for the 2x
 * supersample readback. Navigation math (cursor ray unprojection, pans, zooms)
 * must stay in on-screen panel px while the engine renders into a
 * supersample-scaled render target; this is the single authoritative mapping.
 */

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


namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>Resolved viewport sizes for one tick: panel px (what the user sees
/// and points at) and render-target px (what the engine rasterizes).</summary>
public readonly record struct RenderSize(uint PanelW, uint PanelH, uint RtW, uint RtH);

/// <summary>Computes the panel and render-target pixel sizes.</summary>
public static class RenderSizing
{
    /// <summary>Supersample multiplier applied to the render target when the
    /// Supersample setting is on. A 1px line becomes 2px of SMAA footprint.</summary>
    public const int SupersampleFactor = 2;

    /// <summary>Panel px stay as given; the render target scales by
    /// <see cref="SupersampleFactor"/> when <paramref name="supersample"/>.</summary>
    public static RenderSize Resolve(uint panelW, uint panelH, bool supersample)
    {
        var w = Math.Max(1u, panelW);
        var h = Math.Max(1u, panelH);
        return supersample
            ? new RenderSize(w, h, w * SupersampleFactor, h * SupersampleFactor)
            : new RenderSize(w, h, w, h);
    }
}