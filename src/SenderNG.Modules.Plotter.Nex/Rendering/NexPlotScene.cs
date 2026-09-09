/*
 * NexPlotScene.cs - Nex plot scene: the Vulkan-backed IPlotScene
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
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Scene;
using SenderNG.Modules.Plotter.Nex.Scene;
using Format = HelixToolkit.Nex.Graphics.Format;
using SBounds = SenderNG.Modules.Plotter.Nex.Scene.BoundingBox;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// IPlotScene implemented against a live Nex (Vulkan) engine. Owns the whole
/// render pipeline: device context, engine, world data, cameras/controllers and
/// every node (static chrome plus dynamic build content).
///
/// Because <see cref="OrbitCameraController.Camera"/> is read-only (verified in
/// the spike), a preset swap switches which of the two (camera, controller)
/// pairs is exposed through <see cref="ActiveCameraController"/> so the host
/// view can re-bind it.
///
/// Program reloads: the kinematic factory ends every build with
/// CreateTool, so the next AddCarriagePlane/AddLoftSurface/CreateTool after a
/// completed build provokes a ResetBuild that disposes the previous run's
/// nodes, and the new run's layers become the framing content. The engine is
/// released (mostly) at process exit because the host caches one view per
/// module for the app lifetime.
/// </summary>
public sealed class NexPlotScene : IPlotScene, IDisposable
{
    // Opaque chrome colors (no alpha — premultiply handling is unverified).
    private static readonly Color4 GridMinorColor = new(0.82f, 0.82f, 0.82f, 1f);
    private static readonly Color4 GridMajorColor = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Color4 OriginColor = new(0.53f, 0.53f, 0.53f, 1f);
    private static readonly Color4 WorkEnvelopeColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Color4 JobEnvelopeColor = new(0f, 0.7f, 0.9f, 1f);

    private readonly IContext _context;
    private readonly Engine _engine;
    private readonly WorldDataProvider _worldData;
    private readonly Node _root;

    private readonly OrthographicCamera _orthoCamera;
    private readonly PerspectiveCamera _perspectiveCamera;
    private readonly ICameraController _lockedController;
    private readonly ICameraController _orbitController;
    private ICameraController _activeController;
    private Camera _activeCamera;

    private readonly SceneClient _client;

    private readonly Geometry _gridGeometry;
    private readonly LineNode _gridNode;
    private readonly Geometry _axesGeometry;
    private readonly LineNode _axesNode;
    private readonly Geometry _originGeometry;
    private readonly LineNode _originNode;
    private readonly Geometry _workGeometry;
    private readonly LineNode _workNode;
    private readonly Geometry _jobGeometry;
    private readonly LineNode _jobNode;

    private readonly List<NexCarriagePlaneLayer> _liveLayers = [];
    private readonly List<NexLoftSurface> _liveLofts = [];
    private readonly List<NexPlotTool> _liveTools = [];
    private readonly List<LineNode> _contentNodes = [];
    private bool _buildComplete;

    /// <summary>Create the scene and the whole render pipeline.</summary>
    /// <remarks>
    /// The Vulkan context on Windows must be created with Win32 external memory on the same adapter as
    /// the D3D11 device that the viewport's shared-texture bridge uses. This mirrors the official
    /// Avalonia interop sample exactly.
    /// </remarks>
    public NexPlotScene()
    {
        if (OperatingSystem.IsWindows())
        {
            using var d3d11 = new D3D11DeviceManager();
            _context = VulkanBuilder.CreateHeadless(new VulkanContextConfig
            {
                EnableExternalMemoryWin32 = true,
                RequiredDeviceLuid = d3d11.AdapterLuid,
                EnableValidation = true
            });
        }
        else
        {
            throw new PlatformNotSupportedException("SenderNG plotter Nex scene supports Windows only.");
        }

        _engine = EngineBuilder.Create(_context)
            .WithDefaultNodes(renderToSwapchain: false)
            .WithLine()
            .RenderToCustomTarget(Format.RGBA_UN8)
            .Build();
        _worldData = _engine.CreateWorldDataProvider();
        _worldData.Initialize();

        _root = _worldData.World.CreateNode("PlotScene");

        _orthoCamera = new OrthographicCamera
        {
            Width = 100f,
            NearPlane = 0.01f,
            FarPlane = 10000f,
            Position = new Vector3(0f, 0f, 100f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY
        };
        _perspectiveCamera = new PerspectiveCamera
        {
            NearPlane = 0.01f,
            FarPlane = 10000f,
            Position = new Vector3(0f, 0f, 1f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            Fov = 0.8f
        };
        _lockedController = new OrbitCameraController(_orthoCamera) { RotationSensitivity = 0f };
        _orbitController = new OrbitCameraController(_perspectiveCamera) { RotationSensitivity = 1f };
        _activeController = _orbitController;
        _activeCamera = _perspectiveCamera;

        _client = new SceneClient(_worldData, () => _activeCamera);
        _gridNode = AddChromeNode("grid", 0.6f, out _gridGeometry);
        _axesNode = AddChromeNode("axis-arrows", 1.6f, out _axesGeometry);
        _originNode = AddChromeNode("origin", 1.6f, out _originGeometry);
        _workNode = AddChromeNode("work-envelope", 1.2f, out _workGeometry);
        _jobNode = AddChromeNode("job-envelope", 1.2f, out _jobGeometry);

        SetViewPreset(ViewPreset.Isometric3D);
    }

    /// <summary>Live Nex engine behind this scene (host wires it into the view).</summary>
    public Engine Engine => _engine;

    /// <summary>Viewport client the host's HelixViewport binds to.</summary>
    public IViewportClient ViewportClient => _client;

    /// <summary>Controller the view must bind right now (camera swaps per preset).</summary>
    public ICameraController ActiveCameraController => _activeController;

    /// <inheritdoc/>
    public ICarriagePlaneLayer AddCarriagePlane()
    {
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var layer = new NexCarriagePlaneLayer(_root);
        _liveLayers.Add(layer);
        _contentNodes.Add(layer.Node);
        return layer;
    }

    /// <inheritdoc/>
    public ILoftSurface AddLoftSurface()
    {
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var loft = new NexLoftSurface(_root);
        _liveLofts.Add(loft);
        return loft;
    }

    /// <inheritdoc/>
    public IPlotTool CreateTool(ToolShape shape)
    {
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var tool = new NexPlotTool(_root, shape);
        _liveTools.Add(tool);
        _buildComplete = true;
        return tool;
    }

    /// <inheritdoc/>
    public void SetGrid(SBounds bounds, double minor, double major)
    {
        var g = _gridGeometry;
        g.Vertices.Clear();
        g.VertexColors.Clear();
        if (minor <= 0) minor = 1;
        if (major <= 0) major = minor * 5;

        // Sit clear of the Mill plane so floor lines never z-fight it.
        var z = (float)bounds.MinZ - 0.5f;
        var minX = (float)bounds.MinX;
        var maxX = (float)bounds.MaxX;
        var minY = (float)bounds.MinY;
        var maxY = (float)bounds.MaxY;

        void Line(float x0, float y0, float x1, float y1, Vector4 color)
        {
            g.Vertices.Add(new Vector4(x0, y0, z, 1f));
            g.Vertices.Add(new Vector4(x1, y1, z, 1f));
            g.VertexColors.Add(color);
            g.VertexColors.Add(color);
        }

        for (double x = minX; x <= maxX + 1e-9; x += minor)
        {
            var xx = (float)Math.Min(x, maxX);
            var color = IsOnStep(xx, major) ? GridMajorColor.ToVector4() : GridMinorColor.ToVector4();
            Line(xx, minY, xx, maxY, color);
        }
        for (double y = minY; y <= maxY + 1e-9; y += minor)
        {
            var yy = (float)Math.Min(y, maxY);
            var color = IsOnStep(yy, major) ? GridMajorColor.ToVector4() : GridMinorColor.ToVector4();
            Line(minX, yy, maxX, yy, color);
        }
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    /// <inheritdoc/>
    public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows)
    {
        var g = _axesGeometry;
        g.Vertices.Clear();
        g.VertexColors.Clear();
        var len = AxisArrowLength();
        foreach (var arrow in arrows)
        {
            var color = AxisColor(arrow.Label);
            FillArrow(g, arrow.Pos, AxisDirection(arrow.Label), len, color);
        }
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    /// <inheritdoc/>
    public void SetOrigin(Vector3 origin)
    {
        var g = _originGeometry;
        g.Vertices.Clear();
        g.VertexColors.Clear();
        var tick = OriginTickLength();
        var c = OriginColor.ToVector4();

        void Seg(Vector3 a, Vector3 b)
        {
            g.Vertices.Add(new Vector4(a, 1f));
            g.Vertices.Add(new Vector4(b, 1f));
            g.VertexColors.Add(c);
            g.VertexColors.Add(c);
        }

        Seg(origin - Vector3.UnitX * tick, origin + Vector3.UnitX * tick);
        Seg(origin - Vector3.UnitY * tick, origin + Vector3.UnitY * tick);
        Seg(origin - Vector3.UnitZ * tick, origin + Vector3.UnitZ * tick);
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    /// <inheritdoc/>
    public void SetWorkEnvelope(SBounds box) => FillEnvelope(_workGeometry, box, WorkEnvelopeColor);

    /// <inheritdoc/>
    public void SetJobEnvelope(SBounds box) => FillEnvelope(_jobGeometry, box, JobEnvelopeColor);

    /// <inheritdoc/>
    public void SetViewPreset(ViewPreset preset)
    {
        var (center, radius, span) = ContentMetrics();
        switch (preset)
        {
            case ViewPreset.XY:
                _orthoCamera.Width = MathF.Max(MathF.Max(span.X, span.Y) * 1.4f, 1f);
                _orthoCamera.Position = center + Vector3.UnitZ * (radius * 2f);
                _orthoCamera.Target = center;
                _orthoCamera.Up = Vector3.UnitY;
                Activate(_lockedController, _orthoCamera);
                break;
            case ViewPreset.XZ:
                _orthoCamera.Width = MathF.Max(MathF.Max(span.X, span.Z) * 1.4f, 1f);
                _orthoCamera.Position = center + Vector3.UnitY * (radius * 2f);
                _orthoCamera.Target = center;
                _orthoCamera.Up = Vector3.UnitZ;
                Activate(_lockedController, _orthoCamera);
                break;
            case ViewPreset.YZ:
                _orthoCamera.Width = MathF.Max(MathF.Max(span.Y, span.Z) * 1.4f, 1f);
                _orthoCamera.Position = center + Vector3.UnitX * (-radius * 2f);
                _orthoCamera.Target = center;
                _orthoCamera.Up = Vector3.UnitY;
                Activate(_lockedController, _orthoCamera);
                break;
            default:
                var dir = Vector3.Normalize(new Vector3(1f, 0.75f, 1f));
                _perspectiveCamera.Position = center + dir * (radius * 2.2f);
                _perspectiveCamera.Target = center;
                _perspectiveCamera.Up = Vector3.UnitY;
                _perspectiveCamera.FarPlane = MathF.Max(1000f, radius * 10f);
                Activate(_orbitController, _perspectiveCamera);
                break;
        }
    }

    /// <inheritdoc/>
    public void Pan(PanDelta delta)
    {
        var v = delta.WorldDelta;
        _perspectiveCamera.Position += v;
        _perspectiveCamera.Target += v;
        _orthoCamera.Position += v;
        _orthoCamera.Target += v;
    }

    /// <inheritdoc/>
    /// <remarks>POC: screen-point anchoring is skipped — a plain center zoom.</remarks>
    public void ZoomAround(Vector2 screenPoint, double factor)
    {
        _ = screenPoint;
        if (factor <= 0) return;
        if (ReferenceEquals(_activeCamera, _orthoCamera))
        {
            _orthoCamera.Width = Math.Clamp(_orthoCamera.Width / (float)factor, 0.1f, 100000f);
            return;
        }
        var offset = _perspectiveCamera.Target - _perspectiveCamera.Position;
        var length = offset.Length();
        if (length < 0.01f) return;
        _perspectiveCamera.Position = _perspectiveCamera.Target - Vector3.Normalize(offset) * (length / (float)factor);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var tool in _liveTools)
            tool.Dispose();
        foreach (var loft in _liveLofts)
            loft.Node.Dispose();
        foreach (var layer in _liveLayers)
            layer.Node.Dispose();
        _gridNode.Dispose();
        _axesNode.Dispose();
        _originNode.Dispose();
        _workNode.Dispose();
        _jobNode.Dispose();
        _root.Dispose();
        _worldData.Dispose();
        _engine.Teardown();
        _context.Dispose();
    }

    private void Activate(ICameraController controller, Camera camera)
    {
        _activeController = controller;
        _activeCamera = camera;
    }

    private void ResetBuild()
    {
        foreach (var tool in _liveTools)
            tool.Dispose();
        foreach (var loft in _liveLofts)
            loft.Node.Dispose();
        foreach (var layer in _liveLayers)
            layer.Node.Dispose();
        _liveTools.Clear();
        _liveLofts.Clear();
        _liveLayers.Clear();
        _contentNodes.Clear();
        _buildComplete = false;
    }

    private LineNode AddChromeNode(string name, float thickness, out Geometry geometry)
    {
        geometry = new Geometry(Topology.Line, isDynamic: true);
        var node = _root.World.CreateLineNode(name);
        node.Geometry = geometry;
        node.LineThickness = thickness;
        _root.AddChild(node);
        return node;
    }

    /// <summary>Bounding metrics of the live carriage planes (content).</summary>
    private (Vector3 Center, float Radius, Vector3 Span) ContentMetrics()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var found = false;
        foreach (var node in _contentNodes)
        {
            var g = node.Geometry;
            if (g is null || g.Vertices.Count == 0) continue;
            found = true;
            for (var i = 0; i < g.Vertices.Count; i++)
            {
                var v = g.Vertices[i];
                min = Vector3.Min(min, new Vector3(v.X, v.Y, v.Z));
                max = Vector3.Max(max, new Vector3(v.X, v.Y, v.Z));
            }
        }
        if (!found)
        {
            min = new Vector3(-25f);
            max = new Vector3(25f);
        }
        var center = (min + max) * 0.5f;
        var span = max - min;
        var radius = MathF.Max(span.Length() * 0.5f, 1f);
        return (center, radius, span);
    }

    private float AxisArrowLength()
    {
        var (_, radius, _) = ContentMetrics();
        return Math.Clamp(radius * 0.12f, 4f, 60f);
    }

    private float OriginTickLength()
    {
        var (_, radius, _) = ContentMetrics();
        return Math.Clamp(radius * 0.05f, 1f, 10f);
    }

    private static bool IsOnStep(float value, double step)
    {
        var quotient = value / step;
        return Math.Abs(quotient - Math.Round(quotient)) < 1e-4;
    }

    private static Vector3 AxisDirection(string label) => label switch
    {
        "X" => Vector3.UnitX,
        "-X" => -Vector3.UnitX,
        "Y" => Vector3.UnitY,
        "-Y" => -Vector3.UnitY,
        "Z" => Vector3.UnitZ,
        "-Z" => -Vector3.UnitZ,
        _ => Vector3.UnitX
    };

    private static Color4 AxisColor(string label) => label[0] switch
    {
        'X' => new Color4(0.85f, 0.2f, 0.2f, 1f),
        'Y' => new Color4(0.2f, 0.8f, 0.2f, 1f),
        'Z' => new Color4(0.2f, 0.35f, 0.9f, 1f),
        _ => GridMinorColor
    };

    private static void FillArrow(Geometry g, Vector3 anchor, Vector3 dir, float length, Color4 color)
    {
        var c = color.ToVector4();
        var tip = anchor + dir * length;

        void Seg(Vector3 a, Vector3 b)
        {
            g.Vertices.Add(new Vector4(a, 1f));
            g.Vertices.Add(new Vector4(b, 1f));
            g.VertexColors.Add(c);
            g.VertexColors.Add(c);
        }

        Seg(anchor, tip);
        var back = tip - Vector3.Normalize(dir) * 2.2f;
        Seg(tip, back + Vector3.UnitX);
        Seg(tip, back - Vector3.UnitX);
        Seg(tip, back + Vector3.UnitY);
        Seg(tip, back - Vector3.UnitY);
    }

    private static void FillEnvelope(Geometry g, SBounds box, Color4 color)
    {
        g.Vertices.Clear();
        g.VertexColors.Clear();
        var c = color.ToVector4();
        var min = new Vector3((float)box.MinX, (float)box.MinY, (float)box.MinZ);
        var max = new Vector3((float)box.MaxX, (float)box.MaxY, (float)box.MaxZ);
        var c0 = min;
        var c1 = new Vector3(max.X, min.Y, min.Z);
        var c2 = new Vector3(max.X, max.Y, min.Z);
        var c3 = new Vector3(min.X, max.Y, min.Z);
        var c4 = new Vector3(min.X, min.Y, max.Z);
        var c5 = new Vector3(max.X, min.Y, max.Z);
        var c6 = new Vector3(max.X, max.Y, max.Z);
        var c7 = new Vector3(min.X, max.Y, max.Z);

        void Seg(Vector3 a, Vector3 b)
        {
            g.Vertices.Add(new Vector4(a, 1f));
            g.Vertices.Add(new Vector4(b, 1f));
            g.VertexColors.Add(c);
            g.VertexColors.Add(c);
        }

        Seg(c0, c1); Seg(c1, c2); Seg(c2, c3); Seg(c3, c0); // bottom
        Seg(c4, c5); Seg(c5, c6); Seg(c6, c7); Seg(c7, c4); // top
        Seg(c0, c4); Seg(c1, c5); Seg(c2, c6); Seg(c3, c7); // verticals
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    private sealed class SceneClient : IViewportClient
    {
        private readonly WorldDataProvider _data;
        private readonly Func<Camera> _camera;

        public SceneClient(WorldDataProvider data, Func<Camera> camera)
        {
            _data = data;
            _camera = camera;
        }

        public IRenderDataProvider? DataProvider => _data;

        public ICameraParamsProvider Update(RenderContext context, float deltaTime) => _camera();
    }
}