/*
 * GlNexColourParityTests.cs - Task 7b: whole-scene CAD lighting + ACES
 * tone mapping parity against the Nex engine
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
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Core.Tests;

/// <summary>Task 7b (checkpoint-12): the GL plot scene shades with Nex's CAD
/// pipeline — <c>cadStyleLightingFlat</c> (psPBRTemplate.glsl:374-461) in the
/// mesh fragment stage and the ACES filmic curve (psToneGamma.glsl:25-32) on
/// the tone-mapped passes — so a whole-frame capture lands on the Nex
/// screenshot's measured values. One scene drives all three passes at once:
/// a worldwide loft sheet over open background (the sheet is the frame's only
/// RAW-CAD pass — no tone map, the checkpoint-11 discriminator), the axis
/// cones (CAD + ACES), and a rapid toolpath line (authored colour + ACES).
/// The expected bytes are computed in-process from the cited shader sources
/// (a C# port of the two functions), never hand-rounded. Gated by
/// <see cref="GlFactAttribute"/>.</summary>
[Trait("Category", "GPU")]
public class GlNexColourParityTests
{
    private static readonly byte[] Background = [251, 251, 251, 255];
    private const int Size = 256;

    /// <summary>The Nex sheet colour from <c>NexLoftSurface</c>'s material
    /// (albedo 0.42/0.42/0.54, opacity 0.35 — see checkpoint-11); the scene
    /// pushes the same values in <c>GlPlotScene.DrawLoft</c>.</summary>
    private static readonly Vector3 SheetAlbedo = new(0.42f, 0.42f, 0.54f);

    /// <summary>The measured Nex screenshot cluster for the sheet composited
    /// over open background (checkpoint-11 capture, loft_nex.png).</summary>
    private static readonly Vector3 NexSheetOverBg = new(192f, 192f, 197f);

    /// <summary>The whole-frame parity proof: one XY-preset scene with two
    /// carriage planes (z=0 lower, z=30 upper), a z=15 worldwide sheet and
    /// the axis cones (shaft clamped to 60, so the Z head sits entirely above
    /// the sheet and draws pure). Captured twice — without and with the
    /// sheet — so every composite pixel can be checked against its own
    /// behind-the-sheet destination.</summary>
    [GlFact]
    public unsafe void Sheet_cone_and_line_land_on_the_nex_cad_and_aces_values()
    {
        using var ctx = GlContext.Create();
        using var rb = new GlReadback(ctx, Size, Size);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(Size, Size);

        // Content: x/y ±400, z 0..30 — span (800,800,30), radius 565.9, so
        // the axis-arrow shaft length clamps to 60 (0.12 * radius > 60) and
        // the Z cone (tip z=60, base z=40.8) clears the z=15 sheet entirely.
        var lower = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        lower.AddSegment(
            new LineSegment(new Vector3(-400, -400, 0), new Vector3(400, 400, 0)),
            MotionType.Cut);
        lower.AddSegment(
            new LineSegment(new Vector3(-400, 400, 0), new Vector3(400, -400, 0)),
            MotionType.Cut);
        var upper = (GlCarriagePlaneLayer)scene.AddCarriagePlane();
        upper.AddSegment(
            new LineSegment(new Vector3(-380, -200, 30), new Vector3(380, 200, 30)),
            MotionType.Rapid);

        var loft = (GlLoftSurface)scene.AddLoftSurface();
        var a = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, -1000f, 15f)).ToArray();
        var b = Enumerable.Range(0, 5).Select(i => new Vector3(i * 500f - 1000f, 1000f, 15f)).ToArray();
        loft.SetLoftSheet(a, b);

        scene.PumpCarriagePlanes();
        scene.SetViewPreset(ViewPreset.XY);

        var cam = scene.ActiveCamera;
        var vOrtho = Vector3.Normalize(cam.Target - cam.Position);
        var worldPerPx = cam.Width / Size;

        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = true,
            ShowGrid = true,
            ShowEnvelope = true,
            ShowLoft = false,
        });
        var dst = Capture(rb, scene);
        scene.SetView(new PlotterGlSettings
        {
            ShowOrigin = true,
            ShowGrid = true,
            ShowEnvelope = true,
            ShowLoft = true,
        });
        var outPx = Capture(rb, scene);

        // ---- the sheet: raw CAD (no tone map) blended 0.35 over pure bg ----
        var sheetSrc = CadFlat(new Vector3(0, 0, -1), vOrtho, SheetAlbedo) * 255f;
        var expectedComposite = new Vector3(
            0.35f * sheetSrc.X + 0.65f * Background[0],
            0.35f * sheetSrc.Y + 0.65f * Background[1],
            0.35f * sheetSrc.Z + 0.65f * Background[2]);
        var sheetCount = 0;
        var maxFormulaDev = 0f;
        var maxNexDev = 0f;
        var solvedSum = Vector3.Zero;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i0 = (y * Size + x) * 4;
                if (dst[i0] != Background[0] || dst[i0 + 1] != Background[1]
                    || dst[i0 + 2] != Background[2])
                    continue;                       // something else sat behind
                if (outPx[i0] == Background[0] && outPx[i0 + 1] == Background[1]
                    && outPx[i0 + 2] == Background[2])
                    continue;                       // sheet drew nothing here
                // The cone neighbourhood is excluded: a cone pixel with pure
                // background behind it would otherwise read as a sheet pixel.
                if (Math.Max(Math.Abs(x - 128), Math.Abs(y - 128)) <= 3)
                    continue;
                sheetCount++;
                var got = new Vector3(outPx[i0], outPx[i0 + 1], outPx[i0 + 2]);
                maxFormulaDev = Math.Max(maxFormulaDev, Dev(got, expectedComposite));
                maxNexDev = Math.Max(maxNexDev, Dev(got, NexSheetOverBg));
                solvedSum += (got - new Vector3(
                    0.65f * Background[0], 0.65f * Background[1], 0.65f * Background[2])) / 0.35f;
            }
        }
        var solved = sheetCount > 0 ? solvedSum / sheetCount : Vector3.Zero;
        var acesSrc = Aces(sheetSrc / 255f) * 255f;

        // ---- the Z axis cone: CAD + ACES against the mesh's normal set ----
        var (tip, rotation, scale) = GlPlotScene.AxisConeTransform(
            "Z", Vector3.Zero, AxisShaftLength());
        var model = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(tip);
        Matrix4x4.Invert(ToMat3(model), out var nrmToWorld);
        var coneAlbedo = new Vector3(0.2f, 0.35f, 0.9f);     // AxisColor("Z")
        var candidates = new List<Vector3>();
        foreach (var localN in DenseSkirtNormals())
        {
            var w = Vector3.Transform(localN, nrmToWorld);
            if (w.LengthSquared() < 1e-12f)
                continue;
            w = Vector3.Normalize(w);
            if (Vector3.Dot(w, vOrtho) < 0f)
                w = -w;                                     // engine flip
            candidates.Add(Aces(CadFlat(w, vOrtho, coneAlbedo)) * 255f);
        }
        var coneHits = 0;
        for (var y = 125; y <= 131; y++)
        {
            for (var x = 125; x <= 131; x++)
            {
                var i0 = (y * Size + x) * 4;
                if (IsBackground(outPx, i0))
                    continue;
                var got = new Vector3(outPx[i0], outPx[i0 + 1], outPx[i0 + 2]);
                foreach (var cand in candidates)
                {
                    if (Dev(got, cand) <= 6f)
                    {
                        coneHits++;
                        break;
                    }
                }
            }
        }

        // ---- the rapid line above the sheet: authored colour + ACES ----
        var lineExpected = Aces(new Vector3(
            GlCarriagePlaneLayer.RapidColor.X,
            GlCarriagePlaneLayer.RapidColor.Y,
            GlCarriagePlaneLayer.RapidColor.Z)) * 255f;
        // A point on the rapid segment away from the cone: t = 0.7.
        var lp = new Vector3(-380f + 760f * 0.7f, -200f + 400f * 0.7f, 30f);
        var lpx = Size / 2 + (int)MathF.Round(lp.X / worldPerPx);
        var lpy = Size / 2 - (int)MathF.Round(lp.Y / worldPerPx);
        var lineHits = 0;
        for (var y = lpy - 4; y <= lpy + 4; y++)
        {
            for (var x = lpx - 4; x <= lpx + 4; x++)
            {
                if (x < 0 || x >= Size || y < 0 || y >= Size)
                    continue;
                var i0 = (y * Size + x) * 4;
                var got = new Vector3(outPx[i0], outPx[i0 + 1], outPx[i0 + 2]);
                if (Dev(got, lineExpected) <= 4f)
                    lineHits++;
            }
        }

        Console.WriteLine(
            $"[cad-parity] shaft={AxisShaftLength():F2} camWidth={cam.Width:F2} " +
            $"V={vOrtho} sheetSrc=({sheetSrc.X:F2},{sheetSrc.Y:F2},{sheetSrc.Z:F2}) " +
            $"composite=({expectedComposite.X:F1},{expectedComposite.Y:F1},{expectedComposite.Z:F1}) " +
            $"solved=({solved.X:F2},{solved.Y:F2},{solved.Z:F2}) " +
            $"acesSrc=({acesSrc.X:F1},{acesSrc.Y:F1},{acesSrc.Z:F1}) " +
            $"sheetPx={sheetCount} maxFormulaDev={maxFormulaDev:F1} " +
            $"maxNexDev={maxNexDev:F1} coneHits={coneHits} lineHits={lineHits}");

        // Sheet: enough pure-composite pixels, every one on the formula, and
        // the cluster inside the Nex screenshot's measured ±6.
        Assert.True(sheetCount >= 1000,
            $"precondition: the sheet must cover the open-background pixels, got {sheetCount}");
        Assert.True(maxFormulaDev <= 3f,
            "the sheet must be raw CAD (no tone map) blended 0.35 over its " +
            $"destination — deviation {maxFormulaDev:F1} > 3 from " +
            $"({expectedComposite.X:F1},{expectedComposite.Y:F1},{expectedComposite.Z:F1})");
        Assert.True(maxNexDev <= 6f,
            "the sheet composite must land on the Nex screenshot cluster " +
            $"(192,192,197)±6 — deviation {maxNexDev:F1}");
        // The discriminator, solved per-channel: the measured source cluster
        // must be the raw-CAD one, and far from the tone-mapped one.
        Assert.InRange(solved.X, sheetSrc.X - 4f, sheetSrc.X + 4f);
        Assert.InRange(solved.Y, sheetSrc.Y - 4f, sheetSrc.Y + 4f);
        Assert.InRange(solved.Z, sheetSrc.Z - 4f, sheetSrc.Z + 4f);
        Assert.True(Dev(solved, acesSrc) > 15f,
            $"the sheet source must not be the ACES cluster ({acesSrc.X:F1}," +
            $"{acesSrc.Y:F1},{acesSrc.Z:F1}) — solved ({solved.X:F1},{solved.Y:F1},{solved.Z:F1})");

        // Cone: at least one pixel of the Z head on the CAD+ACES predicted
        // values for a normal the mesh can produce.
        Assert.True(coneHits >= 1,
            "the Z axis cone must carry at least one pixel on the CAD+ACES " +
            $"predicted values for a mesh normal — got {coneHits} hits in the " +
            "7x7 centre box");

        // Line: the rapid stroke above the sheet on ACES(authored).
        Assert.True(lineHits >= 3,
            $"the rapid line must carry its ACES tone-mapped colour — got {lineHits} " +
            $"hits near ({lpx},{lpy}), expected ({lineExpected.X:F0},{lineExpected.Y:F0},{lineExpected.Z:F0})");
    }

    /// <summary>The axis-arrow shaft length the scene derives from these
    /// bounds — <c>clamp(radius * 0.12, 4, 60)</c> over span (800,800,30),
    /// which lands on the 60 clamp.</summary>
    private static float AxisShaftLength()
    {
        var span = new Vector3(800f, 800f, 30f);
        var radius = MathF.Max(span.Length() * 0.5f, 1f);
        return Math.Clamp(radius * 0.12f, 4f, 60f);
    }

    /// <summary>The shared cone's 24 skirt normals, densified across each
    /// adjacent pair — a rasterized pixel interpolates between vertex
    /// normals, so the candidate set must cover the in-between directions,
    /// not only the vertices.</summary>
    private static IEnumerable<Vector3> DenseSkirtNormals()
    {
        var (verts, _) = GlMeshBuilder.BuildCone(
            GlMeshBuilder.DefaultRadius, GlMeshBuilder.DefaultHeight);
        const int segments = 24;
        var skirts = new Vector3[segments];
        for (var i = 0; i < segments; i++)
        {
            var o = (i * 6 + 2) * 6;                    // v2: apex with skirt normal
            skirts[i] = Vector3.Normalize(
                new Vector3(verts[o + 3], verts[o + 4], verts[o + 5]));
        }
        for (var i = 0; i < segments; i++)
        {
            var n0 = skirts[i];
            var n1 = skirts[(i + 1) % segments];
            for (var t = 0f; t < 1f; t += 0.05f)
                yield return Vector3.Normalize(Vector3.Lerp(n0, n1, t));
        }
        yield return Vector3.UnitZ;                     // the base cap normal
    }

    /// <summary>The upper-left 3x3 of a model matrix as a 4x4 — the part the
    /// shader's <c>mat3(uModel)</c> sees.</summary>
    private static Matrix4x4 ToMat3(in Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, 0,
        m.M21, m.M22, m.M23, 0,
        m.M31, m.M32, m.M33, 0,
        0, 0, 0, 1);

    /// <summary>C# port of Nex's <c>cadStyleLightingFlat</c>
    /// (psPBRTemplate.glsl:374-461) with the default PBR material
    /// (ambient 0.01, ao 1, metallic 0, emissive 0 — see
    /// <c>PBRMaterialProperties.DefaultProperties</c>). The view direction is
    /// supplied by the caller (the scene's ortho forward or the per-fragment
    /// perspective direction). Internal so the re-baselined neighbours
    /// (<c>GlLoftSurfaceTests</c>, <c>GlLineShaderTests</c>) compute their
    /// expected bytes from the SAME formula — one port, one truth.</summary>
    internal static Vector3 CadFlat(Vector3 n, Vector3 v, Vector3 albedo)
    {
        if (Vector3.Dot(n, v) < 0f)
            n = -n;                                     // psPBRTemplate.glsl:382-384
        var ndotv = Math.Max(Vector3.Dot(n, v), 0f);
        var key = Vector3.Normalize(v + new Vector3(0.25f, 0.35f, 0.15f));
        var fill = Vector3.Normalize(new Vector3(-0.6f, 0.3f, -0.4f));
        var keyNdotL = Math.Max(Vector3.Dot(n, key), 0f);
        var fillNdotL = Math.Max(Vector3.Dot(n, fill), 0f);
        var keyWrap = keyNdotL * 0.8f + 0.2f;
        var keyDiff = albedo * (keyWrap * 0.75f);
        var fillDiff = albedo * (fillNdotL * 0.2f);
        var h = Vector3.Normalize(v + key);
        var ndoth = Math.Max(Vector3.Dot(n, h), 0f);
        var spec = MathF.Pow(ndoth, 48f) * ((48f + 2f) / 8f);
        const float f0 = 0.04f;                         // mix(0.04, 0.5, metallic=0)
        var fresnel = f0 + (1f - f0) * MathF.Pow(1f - ndotv, 5f);
        var specular = new Vector3(spec * 0.25f * fresnel);
        var edgeDarken = 0.7f + 0.3f * (1f - MathF.Pow(1f - ndotv, 2.5f));
        var rim = MathF.Pow(1f - ndotv, 4f) * 0.15f;
        var ambient = albedo * 0.01f * 0.15f;
        var diffuse = (keyDiff + fillDiff) * edgeDarken;
        return ambient + diffuse + specular + new Vector3(rim);
    }

    /// <summary>C# port of Nex's ACES filmic curve
    /// (psToneGamma.glsl:25-32). Internal — see <see cref="CadFlat"/>.</summary>
    internal static Vector3 Aces(Vector3 x)
    {
        static float F(float v) => Math.Clamp(
            (v * (2.51f * v + 0.03f)) / (v * (2.43f * v + 0.59f) + 0.14f),
            0f, 1f);
        return new Vector3(F(x.X), F(x.Y), F(x.Z));
    }

    /// <summary>The worst per-channel deviation between a pixel and an
    /// expected colour.</summary>
    private static float Dev(Vector3 got, Vector3 expected) => Math.Max(
        Math.Abs(got.X - expected.X),
        Math.Max(Math.Abs(got.Y - expected.Y), Math.Abs(got.Z - expected.Z)));

    private static bool IsBackground(byte[] px, int i) =>
        px[i] == Background[0] && px[i + 1] == Background[1]
        && px[i + 2] == Background[2];

    /// <summary>One frame the way the view tick does it: clear →
    /// RenderFrame into the bound FBO → CopyFrame (resolve + glReadPixels) —
    /// the established capture helper, per-file copy by convention.</summary>
    private static unsafe byte[] Capture(GlReadback rb, GlPlotScene scene)
    {
        rb.BeginFrame(Background);
        scene.RenderFrame(Size, Size);
        var px = new byte[Size * Size * 4];
        fixed (byte* p = px)
            Assert.True(rb.CopyFrame((nint)p, Size * 4));
        return px;
    }
}
