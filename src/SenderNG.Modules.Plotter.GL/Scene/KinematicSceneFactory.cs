/*
 * KinematicSceneFactory.cs - assembles a scene for a given machine kinematic
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
(INCLUDING, NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Numerics;
using CNC.Core.Services;

using SenderNG.Modules.Common.Render;
using SenderNG.Modules.Plotter.GL.Model;

namespace SenderNG.Modules.Plotter.GL.Scene;

/// <summary>Headless scene factory that assembles carriage planes, loft, and
/// tool from the machine's RESOLVED kinematics — plane count, the separation
/// placement and the (plane-less) rotary axes.</summary>
public sealed class KinematicSceneFactory : ISceneFactory
{
    private readonly IPlotScene _scene;
    private readonly List<ICarriagePlaneLayer> _layers = [];
    private IPlotTool? _tool;
    private ILoftSurface? _loft;

    /// <summary>Inject the plot scene to populate.</summary>
    public KinematicSceneFactory(IPlotScene scene) => _scene = scene;

    /// <inheritdoc/>
    public int PlaneCount => _layers.Count;

    /// <inheritdoc/>
    public ICarriagePlaneLayer PlaneLayer(int plane) => _layers[plane];

    /// <inheritdoc/>
    public IPlotTool? Tool => _tool;

    /// <inheritdoc/>
    public ILoftSurface? Loft => _loft;

    /// <inheritdoc/>
    public IPlotScene BuildScene(MachineKinematics kinematics)
    {
        _layers.Clear();
        _tool = null;
        _loft = null;

        // Real topology, not an inference: the machine model already says how
        // many planes exist. A rotary axis is never a plane slot (it lives in
        // OptionalAxes), so it cannot conjure a layer or a loft here.
        var planes = kinematics.Planes.Count;
        for (var i = 0; i < planes; i++)
            _layers.Add(_scene.AddCarriagePlane());

        if (planes >= 2)
            _loft = _scene.AddLoftSurface();

        _tool = _scene.CreateTool(ToolShape.Cone);

        if (planes >= 2)
        {
            // The placeholder wire spans the two planes' placements — the
            // separation on the world axis the planes do not span (world X for a
            // foam tower, whose Length × Height plane is vertical) — so the tool
            // starts on the machine and not on an invented diagonal. Live pose
            // updates replace it immediately.
            var second = PlaneWorldMapper.PlacementFor(kinematics, 1);
            _tool.UpdatePosition(new Endpoint([PlaneWorldMapper.PlacementFor(kinematics, 0), second]));
        }
        else
        {
            _tool.UpdatePosition(new Endpoint([Vector3.Zero]));
        }

        return _scene;
    }
}
