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
/// <see cref="GlShaders"/> programs — grid, job envelope, axis shafts —
/// then the content layers, the tool (Task 6), the cone heads, the loft
/// sheet (Task 7) and finally the orbit-pivot marker: the line passes all
/// precede the triangle passes,
/// deliberately so (Intel MSAA root cause at the call site in
/// <see cref="RenderFrame"/>), all under <c>GL_LEQUAL</c>.
/// </para></summary>
internal sealed unsafe class GlPlotScene : IPlotScene
{
    private const float OrbitSensitivity = 0.005f;

    /// <summary>Nex's orbit-pivot albedo RGB (NexPlotScene.cs:78), alpha baked
    /// into the mesh color the way DrawLoft bakes the sheet's — 0.20, a touch
    /// lighter than Nex's 0.35 opacity so the marker sits quieter over the
    /// loft.</summary>
    private static readonly Vector4 OrbitPivotColor = new(1f, 0.66f, 0.1f, 0.2f);

    /// <summary>Opaque chrome colors (no alpha — premultiply handling is
    /// unverified). Verbatim from Nex's palette.</summary>
    private static readonly Vector4 GridMinorColor = new(0.82f, 0.82f, 0.82f, 1f);
    private static readonly Vector4 GridMajorColor = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 JobEnvelopeColor = new(0f, 0.7f, 0.9f, 1f);

    /// <summary>Task 7b: the mesh pass shades with Nex's CAD pipeline
    /// (<c>cadStyleLightingFlat</c>) against world-space positions/normals, so
    /// the camera state and the tone-map discriminator travel as uniforms —
    /// the fixed light is gone with the port.</summary>
    private const float MeshToneMapOn = 1f;

    private const float MeshToneMapOff = 0f;

    private readonly GlCamera _ortho = new();

    // The perspective camera is born in perspective mode: GlCamera defaults
    // to orthographic and nothing else ever flips the flag, which left the ISO
    // preset projecting ortho with a stray width (review Important #1).
    private readonly GlCamera _persp = new() { IsPerspective = true };
    private GlCamera _active = null!;
    private bool _perspectiveActive;
    private ViewPreset _lastPreset = ViewPreset.Isometric3D;
    private float _orthoZoom = 1f;
    private float _navW = 0f;
    private float _navH = 0f;
    private float _fitOrthoWidth = 1f;
    private float _fitDist = 1f;
    private Vector3? _orbitPivot;
    private float _orbitSphereRadius = 1f;

    // --- chrome GPU state (Task 4) ---
    private readonly GlApi _gl;
    private readonly uint _lineProg;
    private readonly uint _meshProg;
    private readonly int _lineMvp;
    private readonly int _lineViewport;
    private readonly int _lineWidthPx;
    private readonly int _meshMvp;
    private readonly int _meshColor;
    private readonly int _meshModel;
    private readonly int _meshCamPos;
    private readonly int _meshViewDir;
    private readonly int _meshPerspective;
    private readonly int _meshToneMap;
    private readonly LineMesh _gridMinor;
    private readonly LineMesh _gridMajor;
    private readonly LineMesh _axesLines;
    private readonly LineMesh _jobLines;
    private readonly IndexedMesh _cone;
    private readonly IndexedMesh _orbitSphere;
    private readonly List<ConeInstance> _axisCones = [];
    private BoundingBox _gridBounds;
    private bool _hasGridBounds;
    private double _gridMinorStep;
    private double _gridMajorStep;

    /// <summary>Read seam: the bounds the grid chrome currently draws (null
    /// before the first <see cref="SetGrid"/>).</summary>
    internal BoundingBox? ChromeGridBounds => _hasGridBounds ? _gridBounds : null;

    /// <summary>Read seam: the minor/major steps the current grid was built
    /// with (0 before the first <see cref="SetGrid"/>) — the empty state's
    /// fixed 2/10 is assertable without rebuilding line vertices.</summary>
    internal (double Minor, double Major) ChromeGridSteps => (_gridMinorStep, _gridMajorStep);

    /// <summary>Read seam: the box the job-envelope chrome currently draws —
    /// set by <see cref="SetJobEnvelope"/>, including every auto-chrome build,
    /// and cleared to null by <see cref="ClearJobEnvelope"/> when the content
    /// empties. Null means don't draw: <see cref="DrawChrome"/> keys the piece
    /// on it and skips the bind.</summary>
    internal BoundingBox? ChromeEnvelope { get; private set; }
    private bool _disposed;

    /// <summary>The chrome palette — Nex's PER-PIECE line thicknesses, verbatim
    /// (Nex <c>NexPlotScene.cs:265-267</c>: grid 1.6, axis-arrows 3.6,
    /// job-envelope 1.6). Palette values pushed to the expansion shader as
    /// <c>uLineWidthPx</c>, never filtered through the driver's glLineWidth
    /// acceptance. They supersede the plan-era single <c>min(1.5, max)</c>
    /// request: fat-line geometry removed the driver-width constraint.</summary>
    private const float GridChromeWidth = 1.6f;

    private const float AxesChromeWidth = 3.6f;

    private const float EnvelopeChromeWidth = 1.6f;

    /// <summary>What <see cref="DrawChrome"/> pushed as <c>uLineWidthPx</c>
    /// for each chrome piece group on the most recent frame — the width read
    /// seam the palette tests assert through (0 for a group that has not
    /// drawn yet; a toggled-off group keeps its last pushed value).</summary>
    internal (float Grid, float Envelope, float Axes) ChromeWidthsPushed =>
        (_pushedGridWidth, _pushedEnvelopeWidth, _pushedAxesWidth);

    private float _pushedGridWidth;
    private float _pushedEnvelopeWidth;
    private float _pushedAxesWidth;

    /// <summary>Read seam: the orbit-pivot marker is up while an orbit
    /// gesture is open (Nex's Show/HideOrbitPivot pair, gated here on
    /// <see cref="_orbitPivot"/> itself).</summary>
    internal bool OrbitSphereVisible => _orbitPivot is not null;

    /// <summary>Read seam: the marker's world radius, reseeded by every
    /// <see cref="SetViewPreset"/> from the framing metrics.</summary>
    internal float OrbitSphereRadius => _orbitSphereRadius;

    private readonly float[] _acceptedLineSizes;

    /// <summary>Read seam: the line widths this context's probe accepted —
    /// an ascending subset of <see cref="GlLineWidths.Candidates"/>. Evidence
    /// only; rendering never picks a width from it.</summary>
    internal IReadOnlyList<float> AcceptedLineSizes => _acceptedLineSizes;

    /// <summary>Read seam: the one-shot constructor log line (accepted widths
    /// + renderer) the due-diligence doc records — also written to
    /// <see cref="Console"/> exactly once per scene.</summary>
    internal string LineWidthLogLine { get; }

    // --- live content (Task 5) ---
    /// <summary>The carriage plane layers of the current build, in factory
    /// order. Reset as a set on a program reload (Nex's ResetBuild fence).</summary>
    private readonly List<GlCarriagePlaneLayer> _liveLayers = [];

    /// <summary>The live tools of the current build — nex keeps the same list
    /// (<c>NexPlotScene._liveTools</c>). At most one exists at a time: the
    /// end-of-build fence in <see cref="CreateTool"/> resets (and disposes)
    /// before every tool after a completed build. Pumped by
    /// <see cref="PumpToolWire"/>, drawn by <see cref="DrawTools"/>, disposed
    /// by <see cref="ResetBuild"/>/<see cref="Dispose"/>.</summary>
    private readonly List<GlPlotTool> _liveTools = [];

    /// <summary>The live loft sheets of the current build — created by
    /// <see cref="AddLoftSurface"/>, drawn by <see cref="DrawLoft"/> (Task 7),
    /// disposed as a set by <see cref="ResetBuild"/>/<see cref="Dispose"/>
    /// (mirrors <see cref="_liveTools"/>: one sheet per build, replaced with
    /// the rest of the content).</summary>
    private readonly List<GlLoftSurface> _liveLofts = [];

    /// <summary>This frame's render-target size, latched at the top of
    /// <see cref="RenderFrame"/> so the tool draws can bind the line program
    /// with the frame's pixel space (see
    /// <see cref="UseToolWireProgram"/>).</summary>
    private uint _frameRtW;
    private uint _frameRtH;

    /// <summary>End-of-build marker the kinematic factory trips with its final
    /// CreateTool — the next AddCarriagePlane after a completed build resets
    /// the previous run's content (Nex's fence, verbatim).</summary>
    private bool _buildComplete;

    /// <summary>The metrics the chrome was last built from — the
    /// (center, span, plane count, has-content) latch
    /// <see cref="MaybeRebuildChrome"/> compares against. PlaneCount belongs
    /// in it: a second plane staged inside the first plane's bounds moves no
    /// coordinate but must still flip the grid to the multi-plane branch.
    /// HasContent belongs in it too: content whose aggregated bbox is exactly
    /// the ±25 fallback collides with the empty latch on every other field.</summary>
    private (Vector3 Center, Vector3 Span, int PlaneCount, bool HasContent)? _chromeMetrics;

    /// <summary>Nex's <c>_contentFramed</c> latch, verbatim semantics: the
    /// constructor frames the camera once against empty content, so the first
    /// rebuild that finds real content re-applies the current preset against
    /// the content's bounds (checkpoint-4 item 1, superseding Task 5b's
    /// no-auto-reframe for this one frame). Every later rebuild is
    /// geometry-only; the latch resets when the content empties (Nex's
    /// <c>sum == 0</c> branch), so the next load frames again.</summary>
    private bool _contentFramed;

    /// <summary>Spike diagnostic latch (remove before the doc): the one-shot
    /// GL error probe has run, and what it saw. The probe itself only fires
    /// from <see cref="RenderFrame"/> in Debug builds.</summary>
    private bool _glErrorProbed;
    private GLEnum _glErrorProbeResult;

    /// <summary>Step 5g.2 order-guard state: the layer pass drew earlier in
    /// the current <see cref="RenderFrame"/> (<c>_sawLayerDraw</c>, raised by
    /// <see cref="DrawLayers"/>), and the cone pass then ran after it
    /// (<c>_layersDrawnBeforeCones</c>, latched by <see cref="DrawAxisCones"/>).
    /// Both reset at the top of every frame; read through
    /// <see cref="LayersDrawnBeforeCones"/>.</summary>
    private bool _sawLayerDraw;
    private bool _layersDrawnBeforeCones;

    /// <summary>Task 7 order/latch state, same one-shot pattern as the step
    /// 5g guard above: the loft pass drew in the current
    /// <see cref="RenderFrame"/> (<c>_loftDrawn</c>) and it drew after the
    /// cone pass (<c>_loftAfterConePass</c>, latched by
    /// <see cref="DrawLoft"/> from <c>_sawConeDraw</c>, raised by
    /// <see cref="DrawAxisCones"/>). All three reset at the top of every
    /// frame; read through <see cref="LoftDrawnThisFrame"/> /
    /// <see cref="LoftDrawnAfterConePass"/>.</summary>
    private bool _loftDrawn;
    private bool _loftAfterConePass;
    private bool _sawConeDraw;

    /// <summary>Overlay chrome gates — pushed by <see cref="SetView"/> from the
    /// view's toggle bar (runtime-only: geometry stays uploaded, the draw is
    /// skipped). <see cref="ShowRunState"/> also forwards to the layers, which
    /// restage base colors and drop the executed overlay when it goes off.</summary>
    internal bool ShowOrigin { get; private set; } = true;

    internal bool ShowEnvelope { get; private set; } = true;

    internal bool ShowGrid { get; private set; } = true;

    internal bool ShowRunState { get; private set; } = true;

    internal bool ShowLoft { get; private set; } = true;

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
        FatLines = GlShaders.SupportsGeometryShaders(context.Version, gles);
        _lineProg = FatLines
            ? GlShaders.Link(_gl,
                GlShaders.Compile(_gl, gles, ShaderType.VertexShader, GlShaders.LineVertex),
                GlShaders.Compile(_gl, gles, ShaderType.GeometryShader, GlShaders.LineGeometry),
                GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, GlShaders.LineFragmentFat))
            : GlShaders.Link(_gl,
                GlShaders.Compile(_gl, gles, ShaderType.VertexShader, GlShaders.LineVertex),
                GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, GlShaders.LineFragment));
        _meshProg = GlShaders.Link(_gl,
            GlShaders.Compile(_gl, gles, ShaderType.VertexShader, GlShaders.MeshVertex),
            GlShaders.Compile(_gl, gles, ShaderType.FragmentShader, GlShaders.MeshFragment));
        _lineMvp = _gl.GetUniformLocation(_lineProg, "uMvp");
        _lineViewport = _gl.GetUniformLocation(_lineProg, "uViewportPx");
        _lineWidthPx = _gl.GetUniformLocation(_lineProg, "uLineWidthPx");
        _meshMvp = _gl.GetUniformLocation(_meshProg, "uMvp");
        _meshColor = _gl.GetUniformLocation(_meshProg, "uColor");
        _meshModel = _gl.GetUniformLocation(_meshProg, "uModel");
        _meshCamPos = _gl.GetUniformLocation(_meshProg, "uCamPos");
        _meshViewDir = _gl.GetUniformLocation(_meshProg, "uViewDir");
        _meshPerspective = _gl.GetUniformLocation(_meshProg, "uPerspective");
        _meshToneMap = _gl.GetUniformLocation(_meshProg, "uToneMap");

        // Depth test for the whole frame, set once: later draws (the Task 5
        // layers, loft and tool) win an equal-depth test through GL_LEQUAL.
        // Line widths are PALETTE values fed to the expansion shader as
        // uLineWidthPx — glLineWidth is not part of rendering anymore (core
        // drivers can refuse w > 1.0 while claiming [1, 255]). The probe
        // below stays on as per-context EVIDENCE, logged once from this
        // constructor for the due-diligence doc (checkpoint-4 item 4).
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _acceptedLineSizes = GlLineWidths.Probe(_gl);
        LineWidthLogLine =
            $"[Plotter.GL] line widths accepted=[{string.Join(", ", _acceptedLineSizes)}] " +
            $"on \"{context.Renderer}\"";
        Console.WriteLine(LineWidthLogLine);

        _gridMinor = new LineMesh(context);
        _gridMajor = new LineMesh(context);
        _axesLines = new LineMesh(context);
        _jobLines = new LineMesh(context);
        var (coneVerts, coneIdx) = GlMeshBuilder.BuildCone(
            GlMeshBuilder.DefaultRadius, GlMeshBuilder.DefaultHeight);
        _cone = new IndexedMesh(context, coneVerts, coneIdx);
        var (sphereVerts, sphereIdx) = GlMeshBuilder.BuildSphere(1f);
        _orbitSphere = new IndexedMesh(context, sphereVerts, sphereIdx);

        // The scene reaches the view with no content, so the constructor
        // provisions the chrome once — the GL counterpart of Nex's first-frame
        // EnsureChromeUpToDate, and with no content that is Nex's
        // BuildEmptyChrome (fixed origin grid, no envelope). Later pumps
        // re-run this through MaybeRebuildChrome when the staged bounds move.
        BuildEmptyChrome();

        SetViewPreset(ViewPreset.Isometric3D);
    }

    /// <summary>The context this scene renders on. Scene and context are one
    /// lifetime: GlPlotScene holds GL objects created on it, so the view
    /// disposes the scene together with the context (FreeRenderResources) and
    /// rebuilds both on re-attach.</summary>
    internal GlContext Context { get; }

    /// <summary>Whether the line program was linked with the three-stage
    /// fat-line expansion (geometry shader present) — false on GLES
    /// contexts below ES 3.2, which have no geometry shaders and link the
    /// two-stage thin-line program instead (Task 8d). Read seam for tests;
    /// on the thin path <c>uViewportPx</c>/<c>uLineWidthPx</c> do not exist
    /// and <see cref="UseLineProgram"/> skips those uniform sets.</summary>
    internal bool FatLines { get; }

    /// <summary>The camera the next <see cref="RenderFrame"/> projects with and
    /// the one every gesture dispatches through — read seam for the
    /// projection-path and gesture-consistency tests (do not mutate).</summary>
    internal GlCamera ActiveCamera => _active;

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
        ShowLoft = settings.ShowLoft;
        // Run state reaches the planes as staging state (Nex's
        // ApplyRunStateToLayer, minus the parked done node — the GL layer
        // gates its own overlay draw on the same flag).
        foreach (var layer in _liveLayers)
            layer.SetRunStateVisible(ShowRunState);
    }

    /// <summary>Pump the carriage planes' staging into their dynamic buffers
    /// (called at the apply cadence, not every tick). Each layer decides for
    /// itself whether its <see cref="RebuildGate"/> grants an upload. The pump
    /// is also the chrome's bounds checkpoint: whatever the staging now reports,
    /// the auto chrome follows — Nex's "rebuild when bounds change".</summary>
    public void PumpCarriagePlanes()
    {
        foreach (var layer in _liveLayers)
            layer.Pump();
        MaybeRebuildChrome();
    }

    /// <summary>Swap-pump the tool connector wires. Every live tool decides
    /// for itself whether its <see cref="RebuildGate"/> grants an upload —
    /// the Nex pump cadence the view drives every tick. The loft is NOT
    /// pumped: its sheet is static for the life of a build (Task 7 uploads it
    /// once, from the build path that calls <c>SetLoftSheet</c>).</summary>
    public void PumpToolWire()
    {
        foreach (var tool in _liveTools)
            tool.Pump();
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
        // Nex's pivot-sphere size (NexPlotScene.cs:523): 5% of the framing
        // radius, floored so a tiny model still gets a visible marker.
        _orbitSphereRadius = MathF.Max(0.5f, radius * 0.05f);
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
        var (minorVerts, majorVerts) = BuildGridLines(bounds, minor, major);
        _gridMinor.Upload(minorVerts);
        _gridMajor.Upload(majorVerts);
        _gridBounds = bounds;
        _gridMinorStep = minor;
        _gridMajorStep = major;
        _hasGridBounds = true;
    }

    /// <summary>The grid's line vertices, split minor/major the way
    /// <see cref="SetGrid"/> uploads them: one line per step along each axis,
    /// colored per step exactly like Nex. Extracted as a pure function so the
    /// vertex layout (including the far-edge boundaries) is assertable
    /// without a GPU.</summary>
    internal static (float[] Minor, float[] Major) BuildGridLines(
        BoundingBox bounds, double minor, double major)
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

        float? lastX = null;
        for (double x = minX; x <= maxX + 1e-9; x += minor)
        {
            var xx = (float)Math.Min(x, maxX);
            lastX = xx;
            var onMajor = IsOnStep(xx, major);
            AppendLine(onMajor ? majorVerts : minorVerts,
                xx, minY, xx, maxY, z, onMajor ? GridMajorColor : GridMinorColor);
        }
        float? lastY = null;
        for (double y = minY; y <= maxY + 1e-9; y += minor)
        {
            var yy = (float)Math.Min(y, maxY);
            lastY = yy;
            var onMajor = IsOnStep(yy, major);
            AppendLine(onMajor ? majorVerts : minorVerts,
                minX, yy, maxX, yy, z, onMajor ? GridMajorColor : GridMinorColor);
        }

        // Close the frame: when (max - min) is not a step multiple the loops
        // stop short of the far edge, so the grid's Xmax/Ymax boundary lines
        // are appended explicitly — same major/minor coloring rule the clamp
        // applies when a step does land on the boundary (checkpoint-4 item 2).
        if (lastX is { } lx && lx != maxX)
        {
            var onMajor = IsOnStep(maxX, major);
            AppendLine(onMajor ? majorVerts : minorVerts,
                maxX, minY, maxX, maxY, z, onMajor ? GridMajorColor : GridMinorColor);
        }
        if (lastY is { } ly && ly != maxY)
        {
            var onMajor = IsOnStep(maxY, major);
            AppendLine(onMajor ? majorVerts : minorVerts,
                minX, maxY, maxX, maxY, z, onMajor ? GridMajorColor : GridMinorColor);
        }

        return (minorVerts.ToArray(), majorVerts.ToArray());
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
        ChromeEnvelope = box;

        void Seg(Vector3 a, Vector3 b) => AppendSegment(verts, a, b, c);
    }

    /// <inheritdoc/>
    public ICarriagePlaneLayer AddCarriagePlane()
    {
        // Program reloads: the kinematic factory ends every build with
        // CreateTool, so the next AddCarriagePlane after a completed build
        // provokes a ResetBuild that disposes the previous run's layers
        // (Nex's fence, verbatim) — the scene has no "clear" API, and a stale
        // toolpath must not keep drawing behind the new one.
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var layer = new GlCarriagePlaneLayer(Context);
        _liveLayers.Add(layer);
        layer.SetRunStateVisible(ShowRunState);
        return layer;
    }

    /// <inheritdoc/>
    public ILoftSurface AddLoftSurface()
    {
        // Same reload fence as AddCarriagePlane: a loft requested after a
        // completed build provokes a ResetBuild that drops the previous
        // run's content first.
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var loft = new GlLoftSurface(Context);
        _liveLofts.Add(loft);
        // NO end-of-build fence here on purpose: the kinematic factory calls
        // CreateTool after this, and that call owns the build-end marker —
        // AddLoftSurface must not complete a build the factory has not
        // finished yet.
        return loft;
    }

    /// <inheritdoc/>
    public IPlotTool CreateTool(ToolShape shape)
    {
        if (_buildComplete)
            ResetBuild();
        _buildComplete = false;
        var tool = new GlPlotTool(this, shape);
        _liveTools.Add(tool);
        // The factory's end-of-build marker (Nex sets the same flag here).
        _buildComplete = true;
        return tool;
    }

    /// <summary>Dispose the previous run's content (Nex's ResetBuild, GL
    /// subset): fresh fabric is registered on every program reload, so the old
    /// layers, lofts and tools must go. Runs on the render thread with the
    /// context current — same thread every pump and draw uses.</summary>
    private void ResetBuild()
    {
        foreach (var tool in _liveTools)
            tool.Dispose();
        _liveTools.Clear();
        foreach (var layer in _liveLayers)
            layer.Dispose();
        _liveLayers.Clear();
        foreach (var loft in _liveLofts)
            loft.Dispose();
        _liveLofts.Clear();
        _buildComplete = false;
        // Nothing is live anymore: the chrome falls back to the empty-state
        // reference grid the ctor provisions (Nex's BuildEmptyChrome).
        MaybeRebuildChrome();
    }

    /// <summary>Bounding metrics of the live content. The layers' staged
    /// extents win (Nex reads its content-node vertices), so a loaded program
    /// reframes onto the toolpath; with none — the empty state — this is
    /// Nex's not-found envelope (min/max ±25), the default box the presets
    /// frame until geometry lands. Camera AND chrome read these same metrics
    /// (Nex parity): the old grid-bounds fallback here is what aimed the
    /// empty camera at the corner grid's center, (30, 30, −25).</summary>
    private (Vector3 Center, float Radius, Vector3 Span) ContentMetrics()
    {
        Vector3 min, max;
        if (TryGetLayerBounds(out var layerMin, out var layerMax))
        {
            min = layerMin;
            max = layerMax;
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

    /// <summary>Union of every live layer's staged extents — false when no
    /// layer has content yet (empty layer, or none created).</summary>
    private bool TryGetLayerBounds(out Vector3 min, out Vector3 max)
    {
        min = default;
        max = default;
        var found = false;
        foreach (var layer in _liveLayers)
        {
            if (!layer.TryGetBounds(out var b))
                continue;
            var layerLo = new Vector3((float)b.MinX, (float)b.MinY, (float)b.MinZ);
            var layerHi = new Vector3((float)b.MaxX, (float)b.MaxY, (float)b.MaxZ);
            min = found ? Vector3.Min(min, layerLo) : layerLo;
            max = found ? Vector3.Max(max, layerHi) : layerHi;
            found = true;
        }
        return found;
    }

    /// <summary>Re-run the chrome when the content state or the aggregated
    /// bounds moved — the empty state's fixed reference grid and the loaded
    /// program's fitted chrome are both rebuilt through this latch (the
    /// plan's "rebuild when bounds change", wired at the pump and at the
    /// reload fence). Geometry only, with the single exception of Nex's
    /// first-content frame: the first rebuild that finds staged content
    /// re-applies the current preset against those bounds, because the
    /// constructor framed the empty scene (checkpoint-4 item 1, see
    /// <see cref="_contentFramed"/>). Later rebuilds never move the camera —
    /// a reframe on a bounds move is the checkpoint-3 zoom-jump class.</summary>
    private void MaybeRebuildChrome()
    {
        var hasContent = TryGetLayerBounds(out _, out _);
        var (center, _, span) = ContentMetrics();
        if (_chromeMetrics is { } built && built.Center == center && built.Span == span
            && built.PlaneCount == _liveLayers.Count && built.HasContent == hasContent)
            return;
        if (hasContent)
        {
            BuildAutoChrome();
            if (!_contentFramed)
            {
                _contentFramed = true;
                SetViewPreset(_lastPreset);
            }
        }
        else
        {
            // No content: Nex's sum == 0 branch — the fixed empty chrome and
            // the frame-latch reset, no camera move; the next arrival
            // reframes through the branch above.
            BuildEmptyChrome();
            _contentFramed = false;
        }
    }

    /// <summary>Inverse view-projection cached by the last
    /// <see cref="RenderFrame"/> — the matrices the GPU actually drew with, in
    /// panel-pixel NDC for the gesture unprojections.</summary>
    internal Matrix4x4 CurrentInvViewProj() => _invViewProj;

    /// <summary>Draw this frame into the readback's FBO — already bound,
    /// viewport-sized and cleared by <see cref="GlReadback.BeginFrame"/> before
    /// this is called. Recomputes the active camera's view/projection for the
    /// render-target aspect, caches the inverse for cursor-ray placement, then
    /// draws the static chrome (grid → envelope → axis shafts), the content
    /// layers, the tools, the cone heads, last the loft sheet and the
    /// orbit-pivot marker — the line
    /// passes all run before the triangle passes (root cause at its call
    /// below);
    /// <paramref name="rtW"/> / <paramref name="rtH"/> are the render-target
    /// pixels (supersample factor applied), the size the draws must cover.
    /// The order-guard seam for that placement resets here, before any
    /// draw.</summary>
    public void RenderFrame(uint rtW, uint rtH)
    {
        _sawLayerDraw = false;
        _layersDrawnBeforeCones = false;
        _loftDrawn = false;
        _loftAfterConePass = false;
        _sawConeDraw = false;
        _frameRtW = rtW;
        _frameRtH = rtH;
        var view = _active.View();
        var proj = _active.Projection(rtW, rtH);
        _hasInvViewProj = Matrix4x4.Invert(view * proj, out _invViewProj);
        DrawChrome(view * proj, rtW, rtH);
        DrawLayers(view * proj, rtW, rtH);
        // The tool draws sit HERE — after every chrome/layer line pass, before
        // the cone heads — for the same checkpoint-8/9 reason as the cone
        // pass below: on Intel (UHD 630, driver 31.0.101.2137) any in-frame
        // GL_TRIANGLES draw drops multisample coverage for every subsequent
        // draw until the next framebuffer clear. GlPlotTool.Draw issues its
        // wire (GL_LINES) before its cone mesh (GL_TRIANGLES), so this slot
        // keeps the wire ahead of the frame's first triangles and it stays
        // AA; the tool's mesh triangles then run before DrawAxisCones, which
        // loses nothing — all triangle draws are self-hard under MSAA on this
        // driver regardless of position (probe matrix in the SDD ledger).
        // NOT gated by ShowOrigin: Nex gates only the axis nodes with that
        // toggle (NexPlotScene.SetAxesVisible) — the tool nodes live outside
        // it and stay visible on their own UpdatePosition/Pump state.
        DrawTools(view * proj);
        // The cone pass runs after every line pass, and the placement is
        // the whole fix (checkpoint-8; probe matrix in the SDD ledger). WHY: on Intel
        // (UHD 630, driver 31.0.101.2137) any in-frame GL_TRIANGLES draw
        // drops multisample coverage for every subsequent draw until the
        // next framebuffer clear — in the old order (cones inside
        // DrawChrome, ahead of the layers) they poisoned the toolpath's AA
        // while chrome drawn before them kept its own. Checkpoint-7's fix
        // drew a degenerate re-arm line after the cones instead; on hardware
        // every in-frame heal variant failed once the line program had
        // already run that frame (5f's probe matrix), so in-frame healing is
        // a dead end. A triangles draw only poisons draws AFTER it, so the
        // pass sits after every line pass: chrome, layers and the tool wire
        // are unaffected, and the cones lose nothing — their own pixels were
        // already hard-edged under MSAA (0/92 changed None vs X8; the
        // poisoning draw is itself un-antialiased). Documented, not fixed:
        // at equal-depth overlap (the origin region) the cones now win ties
        // because they draw later; and any later triangle pass (the loft,
        // the orbit-pivot marker) must also stay after the line passes — all
        // triangle draws are self-hard under MSAA on this driver regardless
        // of position.
        // Mesa never shows the defect, so this suite proves the ORDER only
        // through the LayersDrawnBeforeCones seam, not pixels. The
        // ShowOrigin gate travels with the pass: the shafts stay gated in
        // DrawChrome and the heads follow the same toggle.
        if (ShowOrigin)
            DrawAxisCones(view * proj);
        // Task 7's loft draws after the cone pass (the orbit-pivot marker
        // follows it): the sheet is the frame's first blend pass and must
        // stay after every line pass (the
        // 5g rule above — a blend composites over a frame that must already
        // hold the lines), and triangle-after-triangle only follows, so
        // nothing loses AA. The sheet itself is self-hard on Intel like all
        // triangle meshes (documented, not fixed).
        DrawLoft(view * proj);
        DrawOrbitPivot(view * proj);
#if DEBUG
        SpikeGlErrorProbe();
#endif
    }

    /// <summary>The tool indicators between the layer pass and the cone
    /// heads — see <see cref="RenderFrame"/> for why this slot exists (the
    /// wire must land before the frame's first triangles). Not gated by
    /// <see cref="ShowOrigin"/> (Nex parity: the tool nodes are independent
    /// of the origin toggle); a parked tool draws nothing from inside
    /// <see cref="GlPlotTool.Draw"/>. The build fence keeps at most one live
    /// tool (see <see cref="_liveTools"/>), so a per-tool wire-then-mesh draw
    /// can never put a second tool's wire behind the first tool's triangles.</summary>
    private void DrawTools(in Matrix4x4 vp)
    {
        if (_liveTools.Count == 0)
            return;
        foreach (var tool in _liveTools)
            tool.Draw(vp);
    }

    /// <summary>Bind the line program for a tool's connector wire: the
    /// frame's MVP and render-target pixel space (latched in
    /// <see cref="RenderFrame"/>) plus the tool-wire palette width
    /// (<see cref="GlPlotTool.WireThickness"/>, Nex's 2.0f node
    /// thickness) as <c>uLineWidthPx</c>.</summary>
    internal void UseToolWireProgram(in Matrix4x4 vp)
        => UseLineProgram(vp, _frameRtW, _frameRtH, GlPlotTool.WireThickness);

    /// <summary>Bind the mesh program for the tool cones — the same program
    /// the axis heads draw through, with the camera uniforms and the
    /// tone-mapped discriminator pushed once per pass; each instance's
    /// MVP/model/color follows via <see cref="SetToolMeshInstance"/>.</summary>
    internal void UseToolMeshProgram()
    {
        _gl.UseProgram(_meshProg);
        PushMeshCamera(MeshToneMapOn);
    }

    /// <summary>Push one tool cone's uniforms: <c>uMvp = model * viewProj</c>
    /// (row-vector order), its model matrix (the CAD pass's world transform)
    /// and the tool palette color.</summary>
    internal void SetToolMeshInstance(in Matrix4x4 model, in Matrix4x4 vp, Vector4 color)
    {
        var m = model;
        var mvp = m * vp;
        _gl.UniformMatrix4(_meshMvp, false,
            MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
        _gl.UniformMatrix4(_meshModel, false,
            MemoryMarshal.CreateReadOnlySpan(ref m.M11, 16));
        _gl.Uniform4(_meshColor, color);
    }

    /// <summary>Push the mesh pass's camera uniforms: the tone-map
    /// discriminator, the camera position (the perspective view direction is
    /// per-fragment <c>normalize(camPos - worldPos)</c>, the engine's
    /// <c>getViewDirection()</c>), the constant ortho view direction
    /// (camera forward <c>normalize(Target - Position)</c>, the task-7b
    /// brief's parallel-projection analogue) and the mode flag that selects
    /// between them.</summary>
    private void PushMeshCamera(float toneMap)
    {
        _gl.Uniform1(_meshToneMap, toneMap);
        _gl.Uniform3(_meshCamPos, _active.Position);
        var forward = _active.Target - _active.Position;
        forward = forward.LengthSquared() > 1e-12f
            ? Vector3.Normalize(forward)
            : -Vector3.UnitZ;
        _gl.Uniform3(_meshViewDir, forward);
        _gl.Uniform1(_meshPerspective, _perspectiveActive ? 1f : 0f);
    }

    /// <summary>The content layers after the chrome (the brief's per-frame
    /// order), through the same line program — each layer binds its own VAO
    /// and draws the plane, then the executed overlay: equal depth, the later
    /// draw wins under the GL_LEQUAL set once in the ctor. Width comes from
    /// Nex's toolpath palette (base/executed 3.2/3.2), pushed as
    /// <c>uLineWidthPx</c> — not from <c>glLineWidth</c>. Reaching this
    /// method with layers to draw raises the order-guard observation the
    /// cone pass latches (see <see cref="LayersDrawnBeforeCones"/>).</summary>
    private void DrawLayers(in Matrix4x4 vp, uint rtW, uint rtH)
    {
        if (_liveLayers.Count == 0)
            return;
        _sawLayerDraw = true;
        UseLineProgram(vp, rtW, rtH, GlCarriagePlaneLayer.BaseLineThickness);
        foreach (var layer in _liveLayers)
            layer.Draw();
    }

    /// <summary>Bind the line program with the frame's shared uniforms:
    /// MVP, and on the fat path also render-target size (the expansion's
    /// pixel space) and the calling role's palette width. The thin-line
    /// fallback (GLES &lt; 3.2, no geometry shader) has no
    /// <c>uViewportPx</c>/<c>uLineWidthPx</c> — plain GL_LINES at the
    /// default driver width.</summary>
    private void UseLineProgram(in Matrix4x4 vp, uint rtW, uint rtH, float widthPx)
    {
        _gl.UseProgram(_lineProg);
        var mvp = vp;
        _gl.UniformMatrix4(_lineMvp, false,
            MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
        if (!FatLines)
            return;
        _gl.Uniform2(_lineViewport, (float)rtW, (float)rtH);
        _gl.Uniform1(_lineWidthPx, widthPx);
    }

    /// <summary>Spike diagnostic (remove before the doc): ONE-SHOT
    /// <c>glGetError</c> in <see cref="RenderFrame"/> — log-and-never-throw,
    /// a GL error must not take the render loop down. Fires on the first frame
    /// that draws layer content (Debug builds only), so the probe covers this
    /// task's new GL calls; chrome-only sessions probe on the first frame.
    /// The result stays readable for the headless test evidence.</summary>
    private void SpikeGlErrorProbe()
    {
        if (_glErrorProbed)
            return;
        var hasLayerContent = false;
        foreach (var layer in _liveLayers)
            hasLayerContent |= layer.HasContent;
        if (!hasLayerContent && _liveLayers.Count > 0)
            return; // layers registered but not pumped yet — wait for content
        _glErrorProbed = true;
        _glErrorProbeResult = _gl.GetError();
        if (_glErrorProbeResult != GLEnum.NoError)
            Console.WriteLine($"plotter-gl spike: GL error reported after a frame: {_glErrorProbeResult}");
    }

    /// <summary>Spike diagnostic read seam: whether the one-shot probe ran.</summary>
    internal bool GlErrorProbeRan => _glErrorProbed;

    /// <summary>Spike diagnostic read seam: what the one-shot probe saw
    /// (meaningless until <see cref="GlErrorProbeRan"/>).</summary>
    internal GLEnum GlErrorProbeResult => _glErrorProbeResult;

    /// <summary>Step 5g.2 order-guard read seam: the most recent
    /// <see cref="RenderFrame"/> ran the layer pass before the cone pass.
    /// Fed only by the actual draw methods — <see cref="DrawLayers"/> raises
    /// the observation, <see cref="DrawAxisCones"/> latches it — and reset
    /// at the top of every frame, so it is false on a frame with no layer
    /// content or no cone pass, and false the moment anyone moves the cones
    /// back ahead of the layers (the checkpoint-7 order).</summary>
    internal bool LayersDrawnBeforeCones => _layersDrawnBeforeCones;

    /// <summary>Task 7 read seam: the most recent <see cref="RenderFrame"/>
    /// ran the loft pass — <see cref="DrawLoft"/> raised it, reset at the top
    /// of every frame, so a frame with <c>ShowLoft</c> off or no sheet with
    /// geometry reads false. Fed only by the actual draw, never by the gate
    /// itself (same discipline as <see cref="LayersDrawnBeforeCones"/>).</summary>
    internal bool LoftDrawnThisFrame => _loftDrawn;

    /// <summary>Task 7 read seam: the most recent frame's loft pass ran AFTER
    /// the cone pass — the checkpoint-8/9 ordering rule extended to the
    /// sheet, which is the frame's only blend pass. False when the loft did
    /// not draw or the cone pass did not run (no cones, or
    /// <c>ShowOrigin</c> off).</summary>
    internal bool LoftDrawnAfterConePass => _loftAfterConePass;

    /// <summary>This frame's chrome, in the brief's per-frame order: grid →
    /// envelope → axis shafts, all under the overlay gates, so the three
    /// toggles off leaves the clear color untouched. The content layers draw
    /// after this, then the tool, then the cone heads last — see
    /// <see cref="RenderFrame"/> for why the line passes must precede the
    /// triangle passes (the GL_LEQUAL set once in the ctor decides its
    /// equal-depth ties).
    /// Each line piece group re-binds the program with ITS OWN palette width
    /// (Nex per-piece parity: grid/envelope 1.6, axis shafts 3.6) as
    /// <c>uLineWidthPx</c> — no <c>glLineWidth</c> state to re-assert.</summary>
    private void DrawChrome(in Matrix4x4 vp, uint rtW, uint rtH)
    {
        if (ShowGrid)
        {
            var width = GridChromeWidth;
            UseLineProgram(vp, rtW, rtH, width);
            _pushedGridWidth = width;
            _gridMinor.Draw();
            _gridMajor.Draw();
        }
        if (ShowEnvelope && ChromeEnvelope is not null)
        {
            var width = EnvelopeChromeWidth;
            UseLineProgram(vp, rtW, rtH, width);
            _pushedEnvelopeWidth = width;
            _jobLines.Draw();
        }
        if (ShowOrigin)
        {
            var width = AxesChromeWidth;
            UseLineProgram(vp, rtW, rtH, width);
            _pushedAxesWidth = width;
            _axesLines.Draw();
        }
    }

    /// <summary>The shaded mesh pass: one shared cone mesh, one model matrix
    /// and one axis color per head (3 heads, 3 uniform sets). The frame's
    /// cone pass, after every line pass (see <see cref="RenderFrame"/> for
    /// why every line pass — chrome, layers and tool wire — must precede the
    /// triangle passes, this one included); reaching it after a layer pass is
    /// what latches the order-guard seam, and running it raises the
    /// <c>_sawConeDraw</c> observation <see cref="DrawLoft"/> latches after
    /// it.</summary>
    private void DrawAxisCones(in Matrix4x4 vp)
    {
        if (_axisCones.Count == 0)
            return;
        _sawConeDraw = true;
        if (_sawLayerDraw)
            _layersDrawnBeforeCones = true;
        _gl.UseProgram(_meshProg);
        PushMeshCamera(MeshToneMapOn);
        for (var i = 0; i < _axisCones.Count; i++)
        {
            var instance = _axisCones[i];
            var model = instance.Model;
            var mvp = model * vp;
            _gl.UniformMatrix4(_meshMvp, false,
                MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
            _gl.UniformMatrix4(_meshModel, false,
                MemoryMarshal.CreateReadOnlySpan(ref model.M11, 16));
            _gl.Uniform4(_meshColor, instance.Color);
            _cone.Draw();
        }
    }

    /// <summary>The frame's loft pass — its first blend pass (the orbit-pivot
    /// marker's follows): the translucent sheet between the carriage planes,
    /// gated by <see cref="ShowLoft"/> and by a live sheet with geometry
    /// (Nex's lazily-absent node — an empty sheet draws nothing).
    /// Pass state:
    /// <c>uMvp = vp</c> with an identity model (the sheet is already in world
    /// space), the CAD camera uniforms with the RAW discriminator
    /// (<c>uToneMap = 0</c> — the checkpoint-12 measurement: Nex's sheet
    /// lands on the un-tone-mapped cluster, so the tone map must NOT touch
    /// this pass), a LIGHTENED sheet colour at 0.15 alpha
    /// (<c>(0.85, 0.85, 0.95)</c> — empirical checkpoint-12/13 fix: hardware
    /// rendered the original 0.42/0.42/0.54 albedo opaque and ~2× too dark;
    /// opacity still lives in the blend so the fragment stage passes
    /// <c>uColor.a</c> through), alpha blending
    /// <c>SrcAlpha / OneMinusSrcAlpha</c> and <c>DepthMask(false)</c> for the
    /// pass, both restored afterwards; the depth TEST is untouched, so the
    /// sheet is still occluded by nearer opaque geometry. Reaching this after
    /// the cone pass is what latches <see cref="LoftDrawnAfterConePass"/>.</summary>
    private void DrawLoft(in Matrix4x4 vp)
    {
        if (!ShowLoft)
            return;
        var hasGeometry = false;
        foreach (var loft in _liveLofts)
        {
            if (loft.HasGeometry)
            {
                hasGeometry = true;
                break;
            }
        }
        if (!hasGeometry)
            return;
        _loftDrawn = true;
        _loftAfterConePass = _sawConeDraw;
        _gl.UseProgram(_meshProg);
        var mvp = vp;
        _gl.UniformMatrix4(_meshMvp, false,
            MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
        var identity = Matrix4x4.Identity;
        _gl.UniformMatrix4(_meshModel, false,
            MemoryMarshal.CreateReadOnlySpan(ref identity.M11, 16));
        PushMeshCamera(MeshToneMapOff);
        // Empirical lighten (checkpoint 12): on hardware the sheet renders opaque
        // at the raw CAD shade, so the albedo carries the whole result — bump
        // it until it visually matches Nex's light sheet. Alpha kept at 0.15
        // so a working blend still composites over the near-white clear.
        _gl.Uniform4(_meshColor, new Vector4(0.85f, 0.85f, 0.95f, 0.15f));
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        foreach (var loft in _liveLofts)
            loft.Draw(vp);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
    }

    /// <summary>The orbit-pivot marker: Nex's translucent amber sphere at the
    /// rotation origin, up exactly while an orbit gesture is open. The
    /// frame's second blend pass and its new last draw, copying DrawLoft's
    /// state discipline — <c>SrcAlpha / OneMinusSrcAlpha</c> and
    /// <c>DepthMask(false)</c> restored after, depth TEST untouched so the
    /// marker is still occluded by nearer opaque geometry. Triangle-after-
    /// triangle is self-hard under MSAA on the Intel quirk (see
    /// <see cref="RenderFrame"/>), and compositing after the loft sheet keeps
    /// the affordance visible over it.</summary>
    private void DrawOrbitPivot(in Matrix4x4 vp)
    {
        if (_orbitPivot is not Vector3 pivot)
            return;
        _gl.UseProgram(_meshProg);
        var model = Matrix4x4.CreateScale(_orbitSphereRadius)
            * Matrix4x4.CreateTranslation(pivot);
        var mvp = model * vp;
        _gl.UniformMatrix4(_meshMvp, false,
            MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
        _gl.UniformMatrix4(_meshModel, false,
            MemoryMarshal.CreateReadOnlySpan(ref model.M11, 16));
        _gl.Uniform4(_meshColor, OrbitPivotColor);
        PushMeshCamera(MeshToneMapOn);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        _orbitSphere.Draw();
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
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
        foreach (var tool in _liveTools)
            tool.Dispose();
        _liveTools.Clear();
        foreach (var layer in _liveLayers)
            layer.Dispose();
        _liveLayers.Clear();
        foreach (var loft in _liveLofts)
            loft.Dispose();
        _liveLofts.Clear();
        _gridMinor.Dispose();
        _gridMajor.Dispose();
        _axesLines.Dispose();
        _jobLines.Dispose();
        _cone.Dispose();
        _orbitSphere.Dispose();
        _gl.DeleteProgram(_lineProg);
        _gl.DeleteProgram(_meshProg);
    }

    // --- chrome geometry (ports of Nex's yellow regions) ---

    /// <summary>Chrome for the no-content state — port of Nex's
    /// <c>BuildEmptyChrome</c> (<c>NexPlotScene.cs:1120-1130</c>): a fixed
    /// square 10×10 reference grid spanning a 100×100 field centered on the
    /// origin at Z=0, origin arrows, and no job envelope — sized to the
    /// empty-content fallback framing (±25) so it fills the view. The metrics
    /// latch records the same (center, span) <see cref="ContentMetrics"/>
    /// reports with no content, so a steady empty state does not rebuild on
    /// every pump.</summary>
    private void BuildEmptyChrome()
    {
        SetGrid(new BoundingBox(-50, -50, 0, 50, 50, 0), minor: 2, major: 10);
        AddAxisArrows(
        [
            (Vector3.Zero, "X"),
            (Vector3.Zero, "Y"),
            (Vector3.Zero, "Z"),
        ]);
        ClearJobEnvelope();
        var (center, _, span) = ContentMetrics();
        _chromeMetrics = (center, span, _liveLayers.Count, false);
    }

    /// <summary>Drop the job-envelope chrome — Nex's <c>ClearNode</c> on the
    /// job geometry, the empty state's "no bounding-box envelopes". The empty
    /// upload zeroes the vertex count (an empty array is a legal upload), and
    /// the null seam keeps <see cref="DrawChrome"/> from binding the piece.
    /// </summary>
    private void ClearJobEnvelope()
    {
        _jobLines.Upload([]);
        ChromeEnvelope = null;
    }

    /// <summary>Provision grid, axes and envelope from the shared
    /// <c>ContentMetrics</c> (camera and chrome agree — Nex parity) — Nex's
    /// <c>BuildAutoChrome</c>, with the plane count taken from
    /// the live carriage planes (Nex passes its plane-node count): one plane
    /// (mill) gets the origin-corner table from machine (0,0), two or more
    /// (foam) the envelope-centered one. Re-runs whenever
    /// <see cref="MaybeRebuildChrome"/> sees the bounds move.</summary>
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
        // One plane (mill) = origin-corner table from machine (0,0); two or
        // more (foam) = grid pinned to the model's XY envelope — Nex passes
        // its plane-node count, the GL scene its live carriage planes.
        var grid = AutoGridBounds(center, span, planeCount: _liveLayers.Count, (float)box.MinZ);
        SetGrid(grid, major / 5, major);
        AddAxisArrows(
        [
            (Vector3.Zero, "X"),
            (Vector3.Zero, "Y"),
            (Vector3.Zero, "Z"),
        ]);
        SetJobEnvelope(box);
        _chromeMetrics = (center, span, _liveLayers.Count, true);
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
}
