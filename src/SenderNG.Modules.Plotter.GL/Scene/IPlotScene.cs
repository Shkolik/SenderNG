/*
 * IPlotScene.cs - top-level scene abstraction for the Nex plotter
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

using System.Numerics;

namespace SenderNG.Modules.Plotter.GL.Scene;

/// <summary>Top-level scene abstraction for the Nex g-code plotter.
/// Headless — no camera types leak in; the adapter applies kinematic transforms.</summary>
public interface IPlotScene : IDisposable
{
    /// <summary>Switch to a preset camera view.</summary>
    void SetViewPreset(ViewPreset preset);

    /// <summary>Pan the scene by a world-space delta.</summary>
    void Pan(PanDelta delta);

    /// <summary>Zoom around a screen-space point by the given factor.</summary>
    void ZoomAround(Vector2 screenPoint, double factor);

    /// <summary>Set the viewport size in physical pixels (basis for all
    /// screen→world conversion).</summary>
    void SetViewportSize(int wPx, int hPx);

    /// <summary>Pan so the model follows the cursor: screen-pixel delta in
    /// physical pixels.</summary>
    void PanByScreenDelta(Vector2 screenDeltaPx);

    /// <summary>Orbit the 3D camera about its target by a screen-pixel delta
    /// (no-op in the rotation-locked orthographic presets).</summary>
    void RotateByScreenDelta(Vector2 screenDeltaPx);

    /// <summary>Begin an orbit gesture: pin the rotation origin to the world
    /// point on the target-depth plane under the cursor. No-op in the
    /// rotation-locked orthographic presets.</summary>
    void BeginOrbit(Vector2 screenPointPx);

    /// <summary>End the orbit gesture; subsequent RotateByScreenDelta calls
    /// no-op until the next BeginOrbit.</summary>
    void EndOrbit();

    /// <summary>Re-run the current preset's framing: reset pan, recenter,
    /// reseed the fit zoom (and return to the iso angle in 3D).</summary>
    void Reframe();

    /// <summary>Add axis arrows with labels to the scene.</summary>
    void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows);

    /// <summary>Configure the grid with minor/major spacing over the given bounds.</summary>
    void SetGrid(BoundingBox bounds, double minor, double major);

    /// <summary>Set the current job envelope.</summary>
    void SetJobEnvelope(BoundingBox box);

    /// <summary>Add a carriage plane layer for plotting segments.</summary>
    ICarriagePlaneLayer AddCarriagePlane();

    /// <summary>Add a loft surface for wire-plane visualization.</summary>
    ILoftSurface AddLoftSurface();

    /// <summary>Create a plot tool with the given shape.</summary>
    IPlotTool CreateTool(ToolShape shape);
}
