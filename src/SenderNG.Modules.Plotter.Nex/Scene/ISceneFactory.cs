/*
 * ISceneFactory.cs - factory interface for per-kinematic scene assembly
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

using CNC.Core.Services;

namespace SenderNG.Modules.Plotter.Nex.Scene;

/// <summary>Assembles a scene for a given machine kinematic type.</summary>
public interface ISceneFactory
{
    /// <summary>Build a fully-assembled scene for the given machine type.</summary>
    IPlotScene BuildScene(MachineType type);

    /// <summary>Number of carriage plane layers in the current scene.</summary>
    int PlaneCount { get; }

    /// <summary>The carriage plane layer for the given plane index.</summary>
    ICarriagePlaneLayer PlaneLayer(int plane);

    /// <summary>The plot tool (null until BuildScene is called).</summary>
    IPlotTool? Tool { get; }

    /// <summary>The loft surface (non-null only for foam / 2+ planes).</summary>
    ILoftSurface? Loft { get; }
}
