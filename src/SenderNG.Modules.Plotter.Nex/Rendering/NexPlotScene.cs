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
using System.Text;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using SenderNG.Modules.Plotter.Nex.Scene;
using Format = HelixToolkit.Nex.Graphics.Format;
using SBounds = SenderNG.Modules.Plotter.Nex.Scene.BoundingBox;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// IPlotScene implemented against a live Nex (Vulkan) engine. Owns the whole
/// render pipeline: device context, engine, world data, cameras and
/// every node (static chrome plus dynamic build content).
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

    private const float OrbitSensitivity = 0.005f;

    private readonly OrthographicCamera _orthoCamera;
    private readonly PerspectiveCamera _perspectiveCamera;
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

    private readonly Geometry _axisConeGeometry;
    private readonly MeshNode[] _axisConeNodes;
    private readonly PBRMaterialProperties[] _axisConeMaterials;

    private readonly List<NexCarriagePlaneLayer> _liveLayers = [];
    private readonly List<NexLoftSurface> _liveLofts = [];
    private readonly List<NexPlotTool> _liveTools = [];
    private readonly List<LineNode> _contentNodes = [];
    private bool _buildComplete;
    private long _lastContentVertices = -1;
    private ViewPreset _preset = ViewPreset.Isometric3D;
    private bool _contentFramed;
    private float _orthoZoom = 1f;
    private float _viewportWpx = 1f;
    private float _viewportHpx = 1f;
    private float _fitOrthoWidth = 1f;
    private float _fitDist = 1f;
    private Vector3? _orbitOrigin;

    /// <summary>Create the scene and the whole render pipeline.</summary>
    /// <remarks>
    /// Plain headless Vulkan on every OS: the readback is a Vulkan-internal
    /// staging copy (IContext.Download), so no external memory, no D3D11
    /// device, and no device-LUID matching are needed. Requires a Vulkan 1.3
    /// device; a missing driver surfaces as VulkanUnavailable in the UI.
    /// </remarks>
    public NexPlotScene()
    {
        _context = VulkanBuilder.CreateHeadless(new VulkanContextConfig
        {
            // False: relying on validation layers being installed would
            // degrade machines without them to the VulkanUnavailable notice.
            EnableValidation = false
        });

        _engine = EngineBuilder.Create(_context)
            .WithDefaultNodes(renderToSwapchain: false)
            .WithLine()
            .WithSMAA()
            .ConfigureNode<SMAANode>(n =>
            {
                n.Quality = SMAAQuality.High;
                n.EdgeDetectionMode = SMAAEdgeDetection.Color;
            })
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
        _activeCamera = _perspectiveCamera;

        _client = new SceneClient(_worldData, () => _activeCamera);
        _gridNode = AddChromeNode("grid", 0.6f, out _gridGeometry);
        _axesNode = AddChromeNode("axis-arrows", 1.6f, out _axesGeometry);
        _originNode = AddChromeNode("origin", 1.6f, out _originGeometry);
        _workNode = AddChromeNode("work-envelope", 1.2f, out _workGeometry);
        _jobNode = AddChromeNode("job-envelope", 1.2f, out _jobGeometry);

        _axisConeGeometry = ConeMesh.Build();
        _engine.Add(_axisConeGeometry);
        _axisConeNodes = new MeshNode[3];
        _axisConeMaterials = new PBRMaterialProperties[3];
        for (var i = 0; i < _axisConeNodes.Length; i++)
        {
            var material = _engine.ResourceManager.PBRPropertyManager.Create(PBRShadingMode.Unlit);
            material.Emissive = Color4.Black;
            material.Opacity = 1f;
            _axisConeMaterials[i] = material;
            var node = _root.World.CreateMeshNode($"axis-cone-{i}");
            node.Geometry = _axisConeGeometry;
            node.MaterialProperties = material;
            node.IsTransparent = true;
            node.Enabled = false;
            _root.AddChild(node);
            _axisConeNodes[i] = node;
        }

        SetViewPreset(ViewPreset.Isometric3D);
    }

    /// <summary>Live Nex engine behind this scene (host wires it into the view).</summary>
    public Engine Engine => _engine;

    /// <summary>Viewport client the host's HelixViewport binds to.</summary>
    public IViewportClient ViewportClient => _client;

    /// <summary>Live orthographic zoom: world units across the view width.
    /// Seeded from the model's bounding-box diagonal on every preset frame
    /// (<see cref="SetViewPreset"/>), then adjusted by the wheel zoom — the
    /// tracked base for the zoom/pan work.</summary>
    public float OrthoZoom => _orthoCamera.Width;

    /// <inheritdoc/>
    public ICarriagePlaneLayer AddCarriagePlane()
    {
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var layer = new NexCarriagePlaneLayer(_root, _engine);
        _liveLayers.Add(layer);
        _contentNodes.Add(layer.Node);
        return layer;
    }

    /// <inheritdoc/>
    public ILoftSurface AddLoftSurface()
    {
        if (DiagnosticDisableLoft)
            return null!;
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var loft = new NexLoftSurface(_root, _engine);
        loft.DiagnosticDisableLoop = DiagnosticDisableLoftLoop;
        loft.DiagnosticDisableSheet = DiagnosticDisableLoftSheet;
        _liveLofts.Add(loft);
        return loft;
    }

    /// <inheritdoc/>
    public IPlotTool CreateTool(ToolShape shape)
    {
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var tool = new NexPlotTool(_root, _engine, shape);
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
        // Re-set the geometry reference so the draw stream refreshes its LineCount.
        _gridNode.Geometry = g;
    }

    /// <inheritdoc/>
    public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows)
    {
        var g = _axesGeometry;
        g.Vertices.Clear();
        g.VertexColors.Clear();
        var len = AxisArrowLength();
        var coneHeight = AxisConeHeight(len);
        for (var i = 0; i < arrows.Count; i++)
        {
            var (pos, label) = arrows[i];
            var color = AxisColor(label);
            FillAxisShaft(g, pos, AxisDirection(label), len, coneHeight, color);
            if (i >= _axisConeNodes.Length)
                continue;
            var (tip, rotation, scale) = AxisConeTransform(label, pos, len);
            var node = _axisConeNodes[i];
            node.Transform.Translation = tip;
            node.Transform.Rotation = rotation;
            node.Transform.Scale = scale;
            node.NotifyTransformChanged();
            node.Enabled = true;
            _axisConeMaterials[i].Albedo = color;
        }
        for (var i = arrows.Count; i < _axisConeNodes.Length; i++)
            _axisConeNodes[i].Enabled = false;
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        // Re-set the geometry reference so the draw stream refreshes its LineCount.
        _axesNode.Geometry = g;
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
        // Re-set the geometry reference so the draw stream refreshes its LineCount.
        _originNode.Geometry = g;
    }

    /// <inheritdoc/>
    public void SetWorkEnvelope(SBounds box)
        => FillEnvelope(_workGeometry, _workNode, box, WorkEnvelopeColor);

    /// <inheritdoc/>
    public void SetJobEnvelope(SBounds box)
        => FillEnvelope(_jobGeometry, _jobNode, box, JobEnvelopeColor);

    /// <inheritdoc/>
    public void SetViewPreset(ViewPreset preset)
    {
        _preset = preset;
        _orbitOrigin = null;
        var (center, radius, span) = ContentMetrics();
        // One zoom basis for every orthographic projection: the model's
        // bounding-box diagonal. Per-plane spans made a long thin board
        // huge in YZ (max(Y,Z) is small) and tiny-consistent ones awkward;
        // a common world-units-per-pixel keeps all projections at the same
        // apparent scale, and is the tracked base for the wheel zoom.
        _orthoZoom = MathF.Max(span.Length() * 1.1f, 1f);
        switch (preset)
        {
            case ViewPreset.XY:
                _orthoCamera.Width = _orthoZoom;
                _orthoCamera.Position = center + Vector3.UnitZ * (radius * 2f);
                _orthoCamera.Target = center;
                _orthoCamera.Up = Vector3.UnitY;
                _activeCamera = _orthoCamera;
                _fitOrthoWidth = _orthoZoom;
                break;
            case ViewPreset.XZ:
                _orthoCamera.Width = _orthoZoom;
                _orthoCamera.Position = center - Vector3.UnitY * (radius * 2f);
                _orthoCamera.Target = center;
                _orthoCamera.Up = Vector3.UnitZ;
                _activeCamera = _orthoCamera;
                _fitOrthoWidth = _orthoZoom;
                break;
            case ViewPreset.YZ:
                _orthoCamera.Width = _orthoZoom;
                _orthoCamera.Position = center + Vector3.UnitX * (radius * 2f);
                _orthoCamera.Target = center;
                _orthoCamera.Up = Vector3.UnitZ;
                _activeCamera = _orthoCamera;
                _fitOrthoWidth = _orthoZoom;
                break;
            default:
                // Skia's iso projector exactly: X -> right/down, Y -> left/down,
                // Z -> straight up. The camera offset must be -normalize(1,1,-1)
                // (front-upper corner); +normalize(1,1,-1) looks from underneath.
                _perspectiveCamera.Position = center
                    + Vector3.Normalize(new Vector3(-1f, -1f, 1f)) * (radius * 2.2f);
                _perspectiveCamera.Target = center;
                _perspectiveCamera.Up = Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f));
                _perspectiveCamera.FarPlane = MathF.Max(1000f, radius * 10f);
                _activeCamera = _perspectiveCamera;
                _fitDist = radius * 2.2f;
                break;
        }
    }

    /// <inheritdoc/>
    public void Pan(PanDelta delta)
    {
        var v = -delta.WorldDelta;
        _perspectiveCamera.Position += v;
        _perspectiveCamera.Target += v;
        _orthoCamera.Position += v;
        _orthoCamera.Target += v;
    }

    /// <inheritdoc/>
    public void ZoomAround(Vector2 screenPoint, double factor)
    {
        if (factor <= 0)
            return;
        if (ReferenceEquals(_activeCamera, _orthoCamera))
            CameraGestures.AnchorZoom(_orthoCamera, screenPoint, factor, _viewportWpx, _viewportHpx);
        else
        {
            CameraGestures.AnchorZoom(_perspectiveCamera, screenPoint, factor, _viewportWpx, _viewportHpx);
        }
    }

    /// <inheritdoc/>
    public void SetViewportSize(int wPx, int hPx)
    {
        _viewportWpx = Math.Max(1, wPx);
        _viewportHpx = Math.Max(1, hPx);
    }

    /// <inheritdoc/>
    public void PanByScreenDelta(Vector2 screenDeltaPx)
    {
        Vector3 v;
        if (ReferenceEquals(_activeCamera, _orthoCamera))
            v = CameraGestures.ScreenPanDelta(_orthoCamera, screenDeltaPx, _viewportWpx, _viewportHpx);
        else
            v = CameraGestures.ScreenPanDelta(_perspectiveCamera, screenDeltaPx, _viewportWpx, _viewportHpx);
        Pan(new PanDelta(v));
    }

    /// <inheritdoc/>
    public void RotateByScreenDelta(Vector2 screenDeltaPx)
    {
        if (ReferenceEquals(_activeCamera, _perspectiveCamera) == false)
            return;
        if (_orbitOrigin is not Vector3 origin)
            return;
        CameraGestures.RotateAroundPoint(
            _perspectiveCamera, origin, screenDeltaPx, OrbitSensitivity, invertY: true, useWorldUp: false);
    }

    /// <inheritdoc/>
    public void BeginOrbit(Vector2 screenPointPx)
    {
        if (ReferenceEquals(_activeCamera, _perspectiveCamera) == false)
        {
            _orbitOrigin = null;
            return;
        }
        var cam = _perspectiveCamera;
        var dist = (cam.Target - cam.Position).Length();
        if (dist < 0.01f)
        {
            _orbitOrigin = cam.Target;
            return;
        }
        CameraGestures.GetCameraBasis(cam, out _, out var right, out var up);
        var wpp = 2f * dist * MathF.Tan(cam.Fov * 0.5f) / MathF.Max(_viewportHpx, 1f);
        var sxW = (screenPointPx.X - _viewportWpx * 0.5f) * wpp;
        var syW = (screenPointPx.Y - _viewportHpx * 0.5f) * wpp;
        _orbitOrigin = cam.Target + right * sxW - up * syW;
    }

    /// <inheritdoc/>
    public void EndOrbit() => _orbitOrigin = null;

    /// <inheritdoc/>
    public void Reframe() => SetViewPreset(_preset);

    /// <summary>Zoom relative to the fitted view (100 = fitted). Ortho scales
    /// the live Width against the preset-seeded fit; perspective scales the
    /// target distance. Zooming in reads &gt;100%.</summary>
    public double ZoomPercent
    {
        get
        {
            if (ReferenceEquals(_activeCamera, _orthoCamera))
            {
                if (_orthoCamera.Width <= 0f)
                    return 100.0;
                return 100.0 * _fitOrthoWidth / _orthoCamera.Width;
            }
            var dist = (_perspectiveCamera.Target - _perspectiveCamera.Position).Length();
            if (dist <= 0f)
                return 100.0;
            return 100.0 * _fitDist / dist;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The viewport's render loop submits asynchronously; destroying any
        // GPU resource the queue may still reference is a use-after-free the
        // driver surfaces as native corruption. Fence all teardown (the
        // reference viewport/bridge do the same before resizing/releasing).
        _engine.WaitForIdle();
        foreach (var tool in _liveTools)
            tool.Dispose();
        foreach (var loft in _liveLofts)
        {
            loft.DisposeMesh();
            loft.Node.Dispose();
        }
        foreach (var layer in _liveLayers)
        {
            layer.ReleaseGeometries();
            layer.Node.Dispose();
        }
        _gridNode.Dispose();
        _axesNode.Dispose();
        for (var i = 0; i < _axisConeNodes.Length; i++)
        {
            _axisConeNodes[i].Dispose();
            _axisConeMaterials[i].Dispose();
        }
        _axisConeGeometry.Dispose();
        _originNode.Dispose();
        _workNode.Dispose();
        _jobNode.Dispose();
        _root.Dispose();
        _worldData.Dispose();
        _engine.Teardown();
        _context.Dispose();
    }

    private void ResetBuild()
    {
        // Same fence as Dispose: the render loop may still be submitting while
        // a program reload swaps the content nodes.
        _engine.WaitForIdle();
        foreach (var tool in _liveTools)
            tool.Dispose();
        foreach (var loft in _liveLofts)
        {
            loft.DisposeMesh();
            loft.Node.Dispose();
        }
        foreach (var layer in _liveLayers)
        {
            layer.ReleaseGeometries();
            layer.Node.Dispose();
        }
        _liveTools.Clear();
        _liveLofts.Clear();
        _liveLayers.Clear();
        _contentNodes.Clear();
        _buildComplete = false;
    }

    private LineNode AddChromeNode(string name, float thickness, out Geometry geometry)
    {
        geometry = new Geometry(Topology.Line, isDynamic: true);
        // Register the geometry with the engine so Geometry.Valid becomes true;
        // otherwise LineDrawInfo.Valid stays false and the node never reaches a
        // draw stream (LineRenderNode.CanRender requires a non-empty stream).
        _engine.Add(geometry);
        var node = _root.World.CreateLineNode(name);
        node.Geometry = geometry;
        node.LineThickness = thickness;
        node.LineColor = Color4.White;
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

    private const float AxisConeHeightFraction = 0.32f;
    private const float AxisConeRadiusFraction = 0.38f;

    internal static float AxisConeHeight(float shaftLength) => shaftLength * AxisConeHeightFraction;

    internal static float AxisConeRadius(float coneHeight) => coneHeight * AxisConeRadiusFraction;

    /// <summary>Shortest-arc rotation carrying the cone's local +Z (its opening
    /// direction) onto <paramref name="target"/>. The ±Z poles are handled
    /// explicitly because the cross product degenerates there.</summary>
    internal static Quaternion RotationFromUnitZTo(Vector3 target)
    {
        target = Vector3.Normalize(target);
        var dot = Vector3.Dot(Vector3.UnitZ, target);
        if (dot > 1f - 1e-6f)
            return Quaternion.Identity;
        if (dot < -1f + 1e-6f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, target));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }

    /// <summary>Apex position, orientation and shared-mesh scale for the cone
    /// tip of an axis arrow. The rotation maps local +Z to -direction so the
    /// apex lands on the shaft tip and the cone opens back toward the anchor.</summary>
    internal static (Vector3 Tip, Quaternion Rotation, Vector3 Scale) AxisConeTransform(
        string label, Vector3 anchor, float shaftLength)
    {
        var dir = AxisDirection(label);
        var tip = anchor + dir * shaftLength;
        var height = AxisConeHeight(shaftLength);
        var radius = AxisConeRadius(height);
        var scale = new Vector3(
            radius / ConeMesh.DefaultRadius,
            radius / ConeMesh.DefaultRadius,
            height / ConeMesh.DefaultHeight);
        return (tip, RotationFromUnitZTo(-dir), scale);
    }

    internal static void FillAxisShaft(
        Geometry g, Vector3 anchor, Vector3 dir, float shaftLength, float coneHeight, Color4 color)
    {
        var c = color.ToVector4();
        var shaftEnd = anchor + dir * MathF.Max(shaftLength - coneHeight, 0f);
        g.Vertices.Add(new Vector4(anchor, 1f));
        g.Vertices.Add(new Vector4(shaftEnd, 1f));
        g.VertexColors.Add(c);
        g.VertexColors.Add(c);
    }

    private static void FillEnvelope(Geometry g, LineNode node, SBounds box, Color4 color)
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
        // Re-set the geometry reference so the draw stream refreshes its LineCount.
        node.Geometry = g;
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

    /// <summary>Render data for one frame (the readback view's per-frame
    /// <c>Engine.RenderOffscreen</c> call).</summary>
    public IRenderDataProvider WorldData => _client.DataProvider!;

    /// <summary>DIAGNOSTIC (temporary): camera framing plus geometry extent, to
    /// check whether content actually falls inside the view frustum (the camera
    /// is framed once in the ctor when content is empty — a stale framing leaves
    /// the whole plot outside the view and nothing draws).</summary>
    public string DumpCamera()
    {
        var cam = _activeCamera;
        var sb = new StringBuilder();
        if (cam is OrthographicCamera ortho)
        {
            sb.Append($"ortho(w={ortho.Width:0.###} pos={ortho.Position} target={ortho.Target} near={ortho.NearPlane} far={ortho.FarPlane}) ");
        }
        else if (cam is PerspectiveCamera persp)
        {
            sb.Append(
                $"persp(fov={persp.Fov:0.###} pos={persp.Position} target={persp.Target} near={persp.NearPlane} far={persp.FarPlane}) "
            );
        }
        else
        {
            sb.Append($"cam({cam.GetType().Name}) ");
        }
        var (center, radius, span) = ContentMetrics();
        sb.Append($"content(center={center} radius={radius:0.###} span={span})");
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var n in _contentNodes)
        {
            var g = n.Geometry;
            if (g is null) continue;
            for (var i = 0; i < g.Vertices.Count; i++)
            {
                var v = g.Vertices[i];
                min = Vector3.Min(min, new Vector3(v.X, v.Y, v.Z));
                max = Vector3.Max(max, new Vector3(v.X, v.Y, v.Z));
            }
        }
        if (min.X == float.MaxValue)
        {
            sb.Append(" ext(none)");
        }
        else
        {
            sb.Append($" ext(min={min} max={max})");
        }
        return sb.ToString();
    }

    /// <summary>DIAGNOSTIC (temporary): when true, the scene disables the GPU
    /// frustum-cull node each frame so geometry visibility no longer depends on
    /// MeshInfo bounds.</summary>
    public bool DiagnosticDisableCulling { get; set; } = true;

    /// <summary>DIAGNOSTIC (temporary): when true, <see cref="AddLoftSurface"/>
    /// returns null so no loft line loop / swept sheet is created. Used to
    /// isolate the tool wire + cone rendering from the loft surface while
    /// chasing the red-fan artifact; re-enable to compare.</summary>
    public bool DiagnosticDisableLoft { get; set; }

    /// <summary>DIAGNOSTIC (temporary): pass the loop-/sheet-disables into each
    /// created <see cref="NexLoftSurface"/> (SetWirePlane vs SetLoftSheet gates).
    /// App A/B to attribute the red-fan artifact to the loop or the sheet.</summary>
    public bool DiagnosticDisableLoftLoop { get; set; }
    public bool DiagnosticDisableLoftSheet { get; set; }

    /// <summary>DIAGNOSTIC (temporary): CPU-side state of every line node owned
    /// by this scene — enabled flag, stream validity, line count, vertex count
    /// and local bounding-sphere radius.</summary>
    public string DumpContent()
    {
        var sb = new StringBuilder();
        void NodeInfo(LineNode n)
        {
            var g = n.Geometry;
            sb.Append(
                $"{n.Name}(en={n.Enabled}, v={n.IsLineValid}, lc={n.LineCount}, vc={(g is null ? -1 : g.Vertices.Count)}, r={(g is null ? -1f : g.BoundingSphereLocal.Radius):F2}, col={n.LineColor.EncodeToUInt():X8}) "
            );
        }

        sb.Append("chrome[ ");
        NodeInfo(_gridNode);
        NodeInfo(_axesNode);
        NodeInfo(_originNode);
        NodeInfo(_workNode);
        NodeInfo(_jobNode);
        sb.Append("] content[ ");
        foreach (var n in _contentNodes)
            NodeInfo(n);
        sb.Append($"] lofts({_liveLofts.Count})[ ");
        foreach (var l in _liveLofts)
            NodeInfo(l.Node);
        sb.Append($"] tools({_liveTools.Count})[ ");
        foreach (var t in _liveTools)
            sb.Append(t.DumpContent() + " ");
        sb.Append("]");
        return sb.ToString();
    }

    /// <summary>Diagnostic (temporary): one executed-overlay summary per layer.</summary>
    public string[] LiveLayerDumps() => [.. _liveLayers.Select(l => l.DumpExecutedOverlay())];

    /// <summary>Swap-pump every carriage plane (see <see cref="NexCarriagePlaneLayer.Pump"/>).
    /// Called from the view's tick loop at a low cadence so run-state color
    /// changes reach the GPU as fresh static geometries instead of in-place
    /// host-visible writes.</summary>
    public void PumpCarriagePlanes() => _liveLayers.ForEach(l => l.Pump());

    /// <summary>Swap-pump the tool connector wires and loft surfaces (see
    /// <see cref="NexLoftSurface.Pump"/> and <see cref="NexPlotTool.Pump"/>). Called
    /// from the view's tick loop so per-status-poll position changes reach the GPU
    /// as fresh static geometries instead of in-place host-visible writes — the
    /// 2-point wire + loft in-place dynamic uploads raced the live render loop and
    /// produced VK_ERROR_DEVICE_LOST on the foam scenes (mill scenes ran wireless).</summary>
    public void PumpToolWireAndLofts()
    {
        foreach (var loft in _liveLofts)
            loft.Pump();
        foreach (var tool in _liveTools)
            tool.Pump();
    }

    /// <summary>The headless Vulkan context the engine renders with.</summary>
    public IContext Context => _context;

    /// <summary>Per-frame camera for the readback view. Abstracted as a method
    /// so the host never needs the Avalonia viewport client interface to tick
    /// the scene.</summary>
    public ICameraParamsProvider UpdateViewport(RenderContext renderContext, float deltaTime)
    {
        EnsureChromeUpToDate();
        return _client.Update(renderContext, deltaTime);
    }

    /// <summary>Rebuild the static chrome (grid, axis arrows, origin, work/job
    /// envelopes) whenever the total content vertex count changes, so the
    /// chrome is always sized to the loaded model even though no caller ever
    /// invokes the Set* chrome methods. The check is O(planes) per frame; the
    /// rebuild is O(verticies) and only happens when content actually moved.</summary>
    private void EnsureChromeUpToDate()
    {
        long sum = 0;
        foreach (var n in _contentNodes)
        {
            var g = n.Geometry;
            if (g is not null)
                sum += g.Vertices.Count;
        }
        if (sum != _lastContentVertices)
        {
            _lastContentVertices = sum;
            if (sum > 0)
            {
                BuildAutoChrome();
                // The ctor frames the camera once against empty content; reframe
                // to the real content on its first arrival so the initial view is
                // centered like the snap-button frames instead of skewed toward
                // the origin.
                if (!_contentFramed)
                {
                    _contentFramed = true;
                    SetViewPreset(_preset);
                }
            }
            else
            {
                // No content: hide the model bounding box and fall back to a
                // fixed reference grid so an unloaded (or never-loaded) plot is
                // a clean 10×10 field instead of the last model's chrome.
                _contentFramed = false;
                BuildEmptyChrome();
            }
        }
    }

    /// <summary>Auto-chrome grid bounds. Single-plane (mill) plots start at machine
    /// (0,0) so the grid runs from the origin 20% past the model's positive extent.
    /// Multi-plane (foam) plots are centered on the unmapped wire axis, so the grid
    /// pins to the model's XY envelope like a work table.</summary>
    internal static SBounds AutoGridBounds(in Vector3 center, in Vector3 span, int planeCount, float z)
    {
        if (planeCount > 1)
        {
            var hx = Math.Max(span.X, 1f) * 0.5f;
            var hy = Math.Max(span.Y, 1f) * 0.5f;
            return new SBounds(center.X - hx, center.Y - hy, z, center.X + hx, center.Y + hy, z);
        }

        return new SBounds(0, 0, z, Math.Max(span.X, 1f) * 1.2, Math.Max(span.Y, 1f) * 1.2, z);
    }

    private void BuildAutoChrome()
    {
        var (center, radius, span) = ContentMetrics();
        var hx = Math.Max(span.X, 1f) * 0.5f;
        var hy = Math.Max(span.Y, 1f) * 0.5f;
        var hz = Math.Max(span.Z, 1f) * 0.5f;
        var box = new SBounds(
            center.X - hx, center.Y - hy, center.Z - hz,
            center.X + hx, center.Y + hy, center.Z + hz
        );
        var major = NiceStep(Math.Max(radius * 0.25, 1));
        // One plane (mill) = origin-corner table from machine (0,0). Two or more
        // planes (foam) = grid pinned to the model's XY envelope like a work table.
        var grid = AutoGridBounds(center, span, _contentNodes.Count, (float)box.MinZ);
        SetGrid(grid, major / 5, major);
        AddAxisArrows(
        [
            (Vector3.Zero, "X"),
            (Vector3.Zero, "Y"),
            (Vector3.Zero, "Z"),
        ]);
        SetOrigin(Vector3.Zero);
        SetWorkEnvelope(box);
        SetJobEnvelope(box);
    }

    /// <summary>Chrome for the no-content state: a fixed square 10×10 reference
    /// grid spanning a 100×100 field with no bounding-box envelopes — sized to
    /// the empty-content fallback framing so it fills the view.</summary>
    private void BuildEmptyChrome()
    {
        SetGrid(new SBounds(-50, -50, 0, 50, 50, 0), minor: 2, major: 10);
        AddAxisArrows(
        [
            (Vector3.Zero, "X"),
            (Vector3.Zero, "Y"),
            (Vector3.Zero, "Z"),
        ]);
        SetOrigin(Vector3.Zero);
        ClearNode(_workGeometry, _workNode);
        ClearNode(_jobGeometry, _jobNode);
    }

    private static void ClearNode(Geometry g, LineNode node)
    {
        g.Vertices.Clear();
        g.VertexColors.Clear();
        g.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        // Re-set the geometry reference so the draw stream refreshes its LineCount.
        node.Geometry = g;
    }

    private static double NiceStep(double raw)
    {
        var exp = Math.Floor(Math.Log10(raw));
        var mag = Math.Pow(10, exp);
        var norm = raw / mag;
        var nice = norm switch
        {
            < 1.5f => 1.0,
            < 3.5f => 2.0,
            < 7.5f => 5.0,
            _ => 10.0
        };
        return nice * mag;
    }
}