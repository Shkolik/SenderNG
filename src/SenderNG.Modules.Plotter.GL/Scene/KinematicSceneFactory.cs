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

namespace SenderNG.Modules.Plotter.GL.Scene;

/// <summary>Headless scene factory that assembles carriage planes, loft, and
/// tool based on the machine's kinematic type.</summary>
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
    public IPlotScene BuildScene(MachineType type)
    {
        _layers.Clear();
        _tool = null;
        _loft = null;

        for (var i = 0; i < type.Planes.Count; i++)
            _layers.Add(_scene.AddCarriagePlane());

        if (type.Planes.Count >= 2)
            _loft = _scene.AddLoftSurface();

        _tool = _scene.CreateTool(ToolShape.Cone);

        if (type.Planes.Count >= 2)
            _tool.UpdatePosition(new Endpoint([Vector3.Zero, Vector3.UnitZ]));
        else
            _tool.UpdatePosition(new Endpoint([Vector3.Zero]));

        return _scene;
    }
}
