/*
 * SceneTypes.cs - enums and records for the Nex scene abstraction
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

namespace SenderNG.Modules.Plotter.Nex.Scene;

/// <summary>Camera view presets: XY/XZ/YZ are locked ortho; Isometric3D enables orbit.</summary>
public enum ViewPreset { XY, XZ, YZ, Isometric3D }

/// <summary>Motion classification for plot segments.</summary>
public enum MotionType { Rapid, Cut }

/// <summary>Execution state of a plot segment.</summary>
public enum RunState { Pending, Executed, InFlight, Error }

/// <summary>Tool tip shapes. POC only uses Cone; others reserved for future.</summary>
public enum ToolShape { Cone, Cylinder, Sphere, Crosshair }

/// <summary>Axis-aligned bounding box.</summary>
public record struct BoundingBox(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);

/// <summary>A straight line between two 3D points.</summary>
public record struct LineSegment(Vector3 From, Vector3 To);

/// <summary>Endpoints of a tool position: 1 point (single-point tool) or 2 (wire).</summary>
public record struct Endpoint(Vector3[] Points);

/// <summary>
/// World-space pan delta. The interface is headless/kinematic — the UI/view-model
/// computes the world-space delta and the adapter applies it; no camera types leak in.
/// </summary>
public record struct PanDelta(Vector3 WorldDelta);
