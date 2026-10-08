/*
 * GlPlotScene.cs - contract-driven plot scene for the GL renderer
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
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>The plot scene the view drives through <see cref="IPlotScene"/>.
/// <para>
/// Navigation is live: two <see cref="GlCamera"/> modes (ortho/perspective),
/// Nex's preset/pan/zoom/orbit wiring retyped onto <see cref="CameraGestures"/>,
/// and the view/projection cached per <see cref="RenderFrame"/> for cursor-ray
/// placement. Each frame draws the static chrome through the two
/// <see cref="GlShaders"/> programs — grid, job envelope, axis shafts and
/// cone heads — in that order, then the content layers, loft and tool (Tasks
/// 5 and 6) on top under <c>GL_LEQUAL</c>.
/// </para></summary>
internal sealed unsafe class GlPlotScene : IPlotScene
{
    private const float OrbitSensitivity = 0.005f;

    /// <summary>Opaque chrome colors (no alpha — premultiply handling is
    /// unverified). Verbatim from Nex's palette.</summary>
    private static readonly Vector4 GridMinorColor = new(0.82f, 0.82f, 0.82f, 1f);
    private static readonly Vector4 GridMajorColor = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 JobEnvelopeColor = new(0f, 0.7f, 0.9f, 1f);

    /// <summary>Fixed light for the shaded mesh pass. The mesh vertex shader
    /// shades against local-space normals (no normal matrix), which is all the
    /// small fixed chrome heads need to read as solid rather than flat.</summary>
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.5f));

    private readonly GlCamera _ortho = new();
    private readonly GlCamera _persp = new();
    private GlCamera _active = null!;
    private bool _perspectiveActive;
    private ViewPreset _lastPreset = ViewPreset.Isometric3D;
    private float _orthoZoom = 1f;
    private float _navW = 0f;
    private float _navH = 0f;
    private float _fitOrthoWidth = 1f;
    private float _fitDist = 1f;
    private Vector3? _orbitPivot;

    // --- chrome GPU state (Task 4) ---
    private readonly GlApi _gl;
    private readonly uint _lineProg;
    private readonly uint _meshProg;
    private readonly int _lineMvp;
    private readonly int _meshMvp;
    private readonly int _meshColor;
    private readonly int _meshLightDir;
    private readonly LineMesh _gridMinor;
    private readonly LineMesh _gridMajor;
    private readonly LineMesh _axesLines;
    private readonly LineMesh _jobLines;
    private readonly IndexedMesh _cone;
    private readonly List<ConeInstance> _axisCones = [];
    private BoundingBox _gridBounds;
    private bool _hasGridBounds;
    private bool _disposed;

    /// <summary>Overlay chrome gates — pushed by <see cref="SetView"/> from the
    /// view's toggle bar (runtime-only: geometry stays uploaded, the draw is
    /// skipped). <see cref="ShowRunState"/> is stored here and starts gating
    /// the executed overlay with the layers in Task 5.</summary>
    internal bool ShowOrigin { get; private set; } = true;

    internal bool ShowEnvelope { get; private set; } = true;

    internal bool ShowGrid { get; private set; } = true;

    internal bool ShowRunState { get; private set; } = true;

    /// <summary>Last frame's render truth for cursor-ray placement (the inverse
    /// view-projection the GPU actually drew with). <c>_hasInvViewProj</c> goes
    /// true after the first <see cref="RenderFrame"/>.</summary>
    private Matrix4x4 _invViewProj;
    private bool _hasInvViewProj;

    /// <summary>Build the chrome GPU state on <paramref name="context"/> and
    /// frame the default view. The context is made current here — the view and
    /// the pixel tests both hand the scene a freshly created one.</summary>
    public GlPlotScene(GlContext context)
    {
        Context = context;
        context.MakeCurrent();
        _gl = context.Api;

        var gles = context.IsGles;
        _lineProg = GlShaders.Link(_gl,
            GlShaders.Compile(_gl, gles, ShaderType.VertexShader, GlShaders.LineVertex),
            GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, GlShaders.LineFragment));
        _meshProg = GlShaders.Link(_gl,
            GlShaders.Compile(_gl, gles, ShaderType.VertexShader, GlShaders.MeshVertex),
            GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, GlShaders.MeshFragment));
        _lineMvp = _gl.GetUniformLocation(_lineProg, "uMvp");
        _meshMvp = _gl.GetUniformLocation(_meshProg, "uMvp");
        _meshColor = _gl.GetUniformLocation(_meshProg, "uColor");
        _meshLightDir = _gl.GetUniformLocation(_meshProg, "uLightDir");

        // Depth test for the whole frame, set once: later draws (the Task 5
        // layers, loft and tool) win an equal-depth test through GL_LEQUAL.
        // Line width: one query, clamped to the driver's own range — most core
        // drivers cap at 1.0, which is fine (the supersampled render target is
        // what buys the coverage).
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        float* lineRange = stackalloc float[2];
        _gl.GetFloat(GetPName.AliasedLineWidthRange, lineRange);
        _gl.LineWidth(MathF.Min(1.5f, lineRange[1]));

        _gridMinor = new LineMesh(context);
        _gridMajor = new LineMesh(context);
        _axesLines = new LineMesh(context);
        _jobLines = new LineMesh(context);
        var (coneVerts, coneIdx) = GlMeshBuilder.BuildCone(
            GlMeshBuilder.DefaultRadius, GlMeshBuilder.DefaultHeight);
        _cone = new IndexedMesh(context, coneVerts, coneIdx);

        // The scene reaches the view with no content (the layers are Task 5), so
        // the constructor provisions the chrome once — the GL counterpart of
        // Nex's EnsureChromeUpToDate first frame. ContentMetrics still reports
        // the not-found envelope at this point: no grid exists yet.
        BuildAutoChrome();

        SetViewPreset(ViewPreset.Isometric3D);
    }

    /// <summary>The context this scene renders on. Scene and context are one
    /// lifetime: GlPlotScene holds GL objects created on it, so the view
    /// disposes the scene together with the context (FreeRenderResources) and
    /// rebuilds both on re-attach.</summary>
    internal GlContext Context { get; }

    /// <summary>Percent readout above the zoom cluster — the fit-seeded
    /// zoom (100 = fitted), hidden at 100% by the view.</summary>
    public float ZoomPercent
    {
        get
        {
            if (_perspectiveActive == false)
            {
                if (_ortho.Width <= 0f)
                    return 100f;
                return 100f * _fitOrthoWidth / _ortho.Width;
            }
            var dist = (_persp.Target - _persp.Position).Length();
            if (dist <= 0f)
                return 100f;
            return 100f * _fitDist / dist;
        }
    }

    /// <summary>Push the overlay view toggles (runtime-only, no rebuild). Each
    /// gate is read in <see cref="RenderFrame"/> — the geometry stays uploaded,
    /// the draw is skipped, which is the Nex node park/enable flip without the
    /// parked transform.</summary>
    public void SetView(PlotterGlSettings settings)
    {
        ShowOrigin = settings.ShowOrigin;
        ShowEnvelope = settings.ShowEnvelope;
        ShowGrid = settings.ShowGrid;
        ShowRunState = settings.ShowRunState;
    }

    /// <summary>Swap-pump the carriage planes into fresh static geometry
    /// (called at the apply cadence, not every tick).</summary>
    public void PumpCarriagePlanes()
    {
    }

    /// <summary>Swap-pump the tool connector wire and loft surface.</summary>
    public void PumpToolWire()
    {
    }

    /// <inheritdoc/>
    public void SetViewPreset(ViewPreset preset)
    {
        _lastPreset = preset;
        _orbitPivot = null;
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
                _ortho.Width = _orthoZoom;
                _ortho.Position = center + Vector3.UnitZ * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitY;
                _active = _ortho;
                _perspectiveActive = false;
                _fitOrthoWidth = _orthoZoom;
                break;
            case ViewPreset.XZ:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center - Vector3.UnitY * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitZ;
                _active = _ortho;
                _perspectiveActive = false;
                _fitOrthoWidth = _orthoZoom;
                break;
            case ViewPreset.YZ:
                _ortho.Width = _orthoZoom;
                _ortho.Position = center + Vector3.UnitX * (radius * 2f);
                _ortho.Target = center;
                _ortho.Up = Vector3.UnitZ;
                _active = _ortho;
                _perspectiveActive = false;
                _fitOrthoWidth = _orthoZoom;
                break;
            default:
                // Skia's iso projector exactly: X -> right/down, Y -> left/down,
                // Z -> straight up. The camera offset must be -normalize(1,1,-1)
                // (front-upper corner); +normalize(1,1,-1) looks from underneath.
                _persp.Position = center
                    + Vector3.Normalize(new Vector3(-1f, -1f, 1f)) * (radius * 2.2f);
                _persp.Target = center;
                _persp.Up = Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f));
                _active = _persp;
                _perspectiveActive = true;
                _fitDist = radius * 2.2f;
                break;
        }
    }

    /// <inheritdoc/>
    public void Pan(PanDelta delta)
    {
        var v = -delta.WorldDelta;
        _persp.Position += v;
        _persp.Target += v;
        _ortho.Position += v;
        _ortho.Target += v;
    }

    /// <inheritdoc/>
    public void ZoomAround(Vector2 screenPoint, double factor)
    {
        if (factor <= 0)
            return;
        CameraGestures.AnchorZoom(_active, screenPoint, factor, _navW, _navH);
    }

    /// <inheritdoc/>
    public void SetViewportSize(int wPx, int hPx)
    {
        // Navigation viewport stays in on-screen (panel) px: cursor px and the
        // pivot/pan/zoom NDC all divide by these, so they must equal what the
        // user points at, not the supersampled render target.
        _navW = Math.Max(1, wPx);
        _navH = Math.Max(1, hPx);
    }

    /// <inheritdoc/>
    public void PanByScreenDelta(Vector2 screenDeltaPx)
    {
        var v = CameraGestures.ScreenPanDelta(_active, screenDeltaPx, _navW, _navH);
        Pan(new PanDelta(v));
    }

    /// <inheritdoc/>
    public void RotateByScreenDelta(Vector2 screenDeltaPx)
    {
        if (_perspectiveActive == false)
            return;
        if (_orbitPivot is not Vector3 origin)
            return;
        CameraGestures.RotateAroundPoint(
            _persp, origin, screenDeltaPx, OrbitSensitivity, invertY: true, useWorldUp: false);
    }

    /// <inheritdoc/>
    public void BeginOrbit(Vector2 screenPointPx)
    {
        if (_perspectiveActive == false)
        {
            _orbitPivot = null;
            return;
        }
        var cam = _persp;
        var dist = (cam.Target - cam.Position).Length();
        if (dist < 0.01f)
        {
            _orbitPivot = cam.Target;
            return;
        }
        if (_hasInvViewProj)
        {
            _orbitPivot = CameraGestures.CursorRayPlanePivot(
                _invViewProj,
                screenPointPx,
                _navW,
                _navH,
                1f,
                cam.Target,
                Vector3.Normalize(cam.Target - cam.Position));
        }
        else
        {
            // Pre-first-frame fallback: no render snapshot yet, so the pivot is
            // the taut safely-degenerate target. The panel has no visible frame
            // before the first tick anyway.
            _orbitPivot = cam.Target;
        }
    }

    /// <inheritdoc/>
    public void EndOrbit()
    {
        _orbitPivot = null;
    }

    /// <inheritdoc/>
    public void Reframe() => SetViewPreset(_lastPreset);

    /// <inheritdoc/>
    public void AddAxisArrows(IReadOnlyList<(Vector3 Pos, string Label)> arrows)
    {
        var shafts = new List<float>();
        var len = AxisArrowLength();
        var coneHeight = AxisConeHeight(len);
        _axisCones.Clear();
        for (var i = 0; i < arrows.Count; i++)
        {
            var (pos, label) = arrows[i];
            var color = AxisColor(label);
            FillAxisShaft(shafts, pos, AxisDirection(label), len, coneHeight, color);
            // Nex heads the first three arrows only — its arrowhead mesh nodes
            // are a preallocated array of three.
            if (i >= AxisConeLimit)
                continue;
            var (tip, rotation, scale) = AxisConeTransform(label, pos, len);
            // Row-vector order: scale, then aim, then place (v * S * R * T).
            var model = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(tip);
            _axisCones.Add(new ConeInstance(model, color));
        }
        _axesLines.Upload(shafts.ToArray());
    }

    /// <inheritdoc/>
    public void SetGrid(BoundingBox bounds, double minor, double major)
    {
        if (minor <= 0) minor = 1;
        if (major <= 0) major = minor * 5;

        // Sit clear of the Mill plane so floor lines never z-fight it.
        var z = (float)bounds.MinZ - 0.5f;
        var minX = (float)bounds.MinX;
        var maxX = (float)bounds.MaxX;
        var minY = (float)bounds.MinY;
        var maxY = (float)bounds.MaxY;

        // One pass, two buffers: a line is minor or major exactly the way Nex
        // colors it per step, it just lands in the mesh that carries that color.
        var minorVerts = new List<float>();
        var majorVerts = new List<float>();

        for (double x = minX; x <= maxX + 1e-9; x += minor)
        {
            var xx = (float)Math.Min(x, maxX);
            var onMajor = IsOnStep(xx, major);
            AppendLine(onMajor ? majorVerts : minorVerts,
                xx, minY, xx, maxY, z, onMajor ? GridMajorColor : GridMinorColor);
        }
        for (double y = minY; y <= maxY + 1e-9; y += minor)
        {
            var yy = (float)Math.Min(y, maxY);
            var onMajor = IsOnStep(yy, major);
            AppendLine(onMajor ? majorVerts : minorVerts,
                minX, yy, maxX, yy, z, onMajor ? GridMajorColor : GridMinorColor);
        }

        _gridMinor.Upload(minorVerts.ToArray());
        _gridMajor.Upload(majorVerts.ToArray());
        _gridBounds = bounds;
        _hasGridBounds = true;
    }

    /// <inheritdoc/>
    public void SetJobEnvelope(BoundingBox box)
    {
        var verts = new List<float>();
        var c = JobEnvelopeColor;
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

        Seg(c0, c1); Seg(c1, c2); Seg(c2, c3); Seg(c3, c0); // bottom
        Seg(c4, c5); Seg(c5, c6); Seg(c6, c7); Seg(c7, c4); // top
        Seg(c0, c4); Seg(c1, c5); Seg(c2, c6); Seg(c3, c7); // verticals
        _jobLines.Upload(verts.ToArray());

        void Seg(Vector3 a, Vector3 b) => AppendSegment(verts, a, b, c);
    }

    public ICarriagePlaneLayer AddCarriagePlane() => new NoopLayer();

    public ILoftSurface AddLoftSurface() => new NoopLoft();

    public IPlotTool CreateTool(ToolShape shape) => new NoopTool();

    /// <summary>Bounding metrics of the live content. Task 5 aggregates the
    /// layer bounds here (Nex reads its content-node vertices); until then the
    /// grid bounds stand in — the only real geometry the scene has — and before
    /// any grid exists this is Nex's not-found envelope (min/max ±25), the
    /// default box the presets frame until geometry lands.</summary>
    private (Vector3 Center, float Radius, Vector3 Span) ContentMetrics()
    {
        Vector3 min, max;
        if (_hasGridBounds)
        {
            var b = _gridBounds;
            min = new Vector3((float)b.MinX, (float)b.MinY, (float)b.MinZ);
            max = new Vector3((float)b.MaxX, (float)b.MaxY, (float)b.MaxZ);
        }
        else
        {
            min = new Vector3(-25f);
            max = new Vector3(25f);
        }
        var center = (min + max) * 0.5f;
        var span = max - min;
        var radius = MathF.Max(span.Length() * 0.5f, 1f);
        return (center, radius, span);
    }

    /// <summary>Inverse view-projection cached by the last
    /// <see cref="RenderFrame"/> — the matrices the GPU actually drew with, in
    /// panel-pixel NDC for the gesture unprojections.</summary>
    internal Matrix4x4 CurrentInvViewProj() => _invViewProj;

    /// <summary>Draw this frame into the readback's FBO — already bound,
    /// viewport-sized and cleared by <see cref="GlReadback.BeginFrame"/> before
    /// this is called. Recomputes the active camera's view/projection for the
    /// render-target aspect, caches the inverse for cursor-ray placement, then
    /// draws the static chrome (grid → envelope → axis shafts → cone heads);
    /// <paramref name="rtW"/> / <paramref name="rtH"/> are the render-target
    /// pixels (supersample factor applied), the size the draws must cover.</summary>
    public void RenderFrame(uint rtW, uint rtH)
    {
        var view = _active.View();
        var proj = _active.Projection(rtW, rtH);
        _hasInvViewProj = Matrix4x4.Invert(view * proj, out _invViewProj);
        DrawChrome(view * proj);
    }

    /// <summary>This frame's chrome, in the brief's per-frame order: grid →
    /// envelope → axis shafts → cone heads, all under the overlay gates, so the
    /// three toggles off leaves the clear color untouched. The Task 5 layers,
    /// loft and tool draw after this on top (GL_LEQUAL, set once in the ctor).</summary>
    private void DrawChrome(in Matrix4x4 vp)
    {
        if (ShowGrid || ShowEnvelope || ShowOrigin)
        {
            _gl.UseProgram(_lineProg);
            var mvp = vp;
            _gl.UniformMatrix4(_lineMvp, false,
                MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
            if (ShowGrid)
            {
                _gridMinor.Draw();
                _gridMajor.Draw();
            }
            if (ShowEnvelope)
                _jobLines.Draw();
            if (ShowOrigin)
                _axesLines.Draw();
        }
        if (ShowOrigin)
            DrawAxisCones(vp);
    }

    /// <summary>The shaded mesh pass: one shared cone mesh, one model matrix
    /// and one axis color per head (3 heads, 3 uniform sets).</summary>
    private void DrawAxisCones(in Matrix4x4 vp)
    {
        if (_axisCones.Count == 0)
            return;
        _gl.UseProgram(_meshProg);
        _gl.Uniform3(_meshLightDir, LightDirection);
        for (var i = 0; i < _axisCones.Count; i++)
        {
            var instance = _axisCones[i];
            var mvp = instance.Model * vp;
            _gl.UniformMatrix4(_meshMvp, false,
                MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
            _gl.Uniform4(_meshColor, instance.Color);
            _cone.Draw();
        }
    }

    /// <summary>Free the GL objects this scene created. Scene and context are
    /// one lifetime: once the context is already gone the driver released
    /// everything, and there is no API left to call.</summary>
    public void Dispose()
    {
        if (_disposed || Context.IsDisposed)
            return;
        _disposed = true;
        Context.MakeCurrent();
        _gridMinor.Dispose();
        _gridMajor.Dispose();
        _axesLines.Dispose();
        _jobLines.Dispose();
        _cone.Dispose();
        _gl.DeleteProgram(_lineProg);
        _gl.DeleteProgram(_meshProg);
    }

    // --- chrome geometry (ports of Nex's yellow regions) ---

    /// <summary>Provision grid, axes and envelope from the framing metrics —
    /// Nex's <c>BuildAutoChrome</c> with a fixed <c>planeCount: 0</c> (the GL
    /// scene holds no plane list; layers are Task 5), so the mill rule applies:
    /// an origin-corner table from machine (0,0).</summary>
    private void BuildAutoChrome()
    {
        var (center, radius, span) = ContentMetrics();
        var hx = Math.Max(span.X, 1f) * 0.5f;
        var hy = Math.Max(span.Y, 1f) * 0.5f;
        var hz = Math.Max(span.Z, 1f) * 0.5f;
        var box = new BoundingBox(
            center.X - hx, center.Y - hy, center.Z - hz,
            center.X + hx, center.Y + hy, center.Z + hz);
        var major = NiceStep(Math.Max(radius * 0.25, 1));
        var grid = AutoGridBounds(center, span, planeCount: 0, (float)box.MinZ);
        SetGrid(grid, major / 5, major);
        AddAxisArrows(
        [
            (Vector3.Zero, "X"),
            (Vector3.Zero, "Y"),
            (Vector3.Zero, "Z"),
        ]);
        SetJobEnvelope(box);
    }

    /// <summary>Auto-chrome grid bounds, verbatim from Nex: single-plane (mill)
    /// plots start at machine (0,0) and run 20% past the model's positive
    /// extent; multi-plane (foam) plots pin to the model's XY envelope.</summary>
    internal static BoundingBox AutoGridBounds(in Vector3 center, in Vector3 span, int planeCount, float z)
    {
        if (planeCount > 1)
        {
            var hx = Math.Max(span.X, 1f) * 0.5f;
            var hy = Math.Max(span.Y, 1f) * 0.5f;
            return new BoundingBox(center.X - hx, center.Y - hy, z, center.X + hx, center.Y + hy, z);
        }

        return new BoundingBox(0, 0, z, Math.Max(span.X, 1f) * 1.2, Math.Max(span.Y, 1f) * 1.2, z);
    }

    /// <summary>Round a raw grid step up to the next 1/2/5×10ⁿ — verbatim from
    /// Nex; the minor step is always this divided by five.</summary>
    internal static double NiceStep(double raw)
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

    private float AxisArrowLength()
    {
        var (_, radius, _) = ContentMetrics();
        return Math.Clamp(radius * 0.12f, 4f, 60f);
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

    private static Vector4 AxisColor(string label) => label[0] switch
    {
        'X' => new Vector4(0.85f, 0.2f, 0.2f, 1f),
        'Y' => new Vector4(0.2f, 0.8f, 0.2f, 1f),
        'Z' => new Vector4(0.2f, 0.35f, 0.9f, 1f),
        _ => GridMinorColor
    };

    private const float AxisConeHeightFraction = 0.32f;
    private const float AxisConeRadiusFraction = 0.38f;

    /// <summary>Nex's arrowhead node count — the heads it preallocates, so only
    /// the first three arrows get one.</summary>
    private const int AxisConeLimit = 3;

    internal static float AxisConeHeight(float shaftLength) => shaftLength * AxisConeHeightFraction;

    internal static float AxisConeRadius(float coneHeight) => coneHeight * AxisConeRadiusFraction;

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
            radius / GlMeshBuilder.DefaultRadius,
            radius / GlMeshBuilder.DefaultRadius,
            height / GlMeshBuilder.DefaultHeight);
        return (tip, ConeOrientation.FromUnitZTo(-dir), scale);
    }

    /// <summary>The shaft segment from the anchor to the cone's base — the
    /// head itself covers the last <paramref name="coneHeight"/>.</summary>
    private static void FillAxisShaft(
        List<float> verts, Vector3 anchor, Vector3 dir, float shaftLength, float coneHeight, Vector4 color)
    {
        var shaftEnd = anchor + dir * MathF.Max(shaftLength - coneHeight, 0f);
        AppendSegment(verts, anchor, shaftEnd, color);
    }

    private static void AppendLine(List<float> dst,
        float x0, float y0, float x1, float y1, float z, Vector4 color)
    {
        AppendVertex(dst, new Vector3(x0, y0, z), color);
        AppendVertex(dst, new Vector3(x1, y1, z), color);
    }

    private static void AppendSegment(List<float> dst, Vector3 a, Vector3 b, Vector4 color)
    {
        AppendVertex(dst, a, color);
        AppendVertex(dst, b, color);
    }

    /// <summary>One interleaved line vertex: <c>vec3 pos + vec4 color</c>,
    /// stride 28.</summary>
    private static void AppendVertex(List<float> dst, Vector3 p, Vector4 c)
    {
        dst.Add(p.X);
        dst.Add(p.Y);
        dst.Add(p.Z);
        dst.Add(c.X);
        dst.Add(c.Y);
        dst.Add(c.Z);
        dst.Add(c.W);
    }

    /// <summary>One axis-head instance: its model matrix and axis color.</summary>
    private readonly record struct ConeInstance(Matrix4x4 Model, Vector4 Color);

    /// <summary>One chrome line piece: a VAO + VBO of interleaved
    /// <c>vec3 pos + vec4 color</c> (stride 28) drawn as GL_LINES. Each piece
    /// rebinds its own VAO, so draw order is free.</summary>
    private sealed class LineMesh : IDisposable
    {
        private readonly GlContext _ctx;
        private readonly GlApi _gl;
        private uint _vao;
        private uint _vbo;
        private int _count;
        private bool _disposed;

        internal LineMesh(GlContext ctx)
        {
            _ctx = ctx;
            ctx.MakeCurrent();
            _gl = ctx.Api;
            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, IntPtr.Zero);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, (IntPtr)12);
            _gl.EnableVertexAttribArray(1);
            _gl.BindVertexArray(0);
        }

        internal void Upload(float[] data)
        {
            if (_disposed || _ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            _count = data.Length / 7;
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data.AsSpan(), BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        }

        internal void Draw()
        {
            if (_disposed || _count == 0 || _ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_count);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            if (_vbo != 0)
                _gl.DeleteBuffer(_vbo);
            if (_vao != 0)
                _gl.DeleteVertexArray(_vao);
            _vbo = 0;
            _vao = 0;
        }
    }

    /// <summary>One indexed mesh (the shared axis cone): interleaved
    /// <c>vec3 pos + vec3 normal</c> (stride 24) with a triangle-list element
    /// buffer, drawn through the mesh program.</summary>
    private sealed class IndexedMesh : IDisposable
    {
        private readonly GlContext _ctx;
        private readonly GlApi _gl;
        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private int _count;
        private bool _disposed;

        internal IndexedMesh(GlContext ctx, float[] verts, uint[] idx)
        {
            _ctx = ctx;
            ctx.MakeCurrent();
            _gl = ctx.Api;
            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _ebo = _gl.GenBuffer();
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, verts.AsSpan(), BufferUsageARB.StaticDraw);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, IntPtr.Zero);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, (IntPtr)12);
            _gl.EnableVertexAttribArray(1);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, idx.AsSpan(), BufferUsageARB.StaticDraw);
            _gl.BindVertexArray(0);
            _count = idx.Length;
        }

        internal void Draw()
        {
            if (_disposed || _count == 0 || _ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            _gl.BindVertexArray(_vao);
            // Offset 0 into the bound element buffer: the pointer is a byte
            // offset there, so NULL is the first index, not a client pointer.
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_count,
                DrawElementsType.UnsignedInt, (void*)0);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_ctx.IsDisposed)
                return;
            _ctx.MakeCurrent();
            if (_ebo != 0)
                _gl.DeleteBuffer(_ebo);
            if (_vbo != 0)
                _gl.DeleteBuffer(_vbo);
            if (_vao != 0)
                _gl.DeleteVertexArray(_vao);
            _ebo = 0;
            _vbo = 0;
            _vao = 0;
        }
    }

    private sealed class NoopLayer : ICarriagePlaneLayer
    {
        public void AddSegment(LineSegment seg, MotionType type)
        {
        }

        public void SetSegmentRunState(int segmentIndex, RunState state)
        {
        }

        public void RebuildExecuted()
        {
        }
    }

    private sealed class NoopLoft : ILoftSurface
    {
        public void SetWirePlane(Vector3[] polygon)
        {
        }

        public void SetLoftSheet(IReadOnlyList<Vector3> sideA, IReadOnlyList<Vector3> sideB)
        {
        }
    }

    private sealed class NoopTool : IPlotTool
    {
        public ToolShape Shape { get; set; }

        public void UpdatePosition(Endpoint endpoints)
        {
        }

        public void Dispose()
        {
        }
    }
}