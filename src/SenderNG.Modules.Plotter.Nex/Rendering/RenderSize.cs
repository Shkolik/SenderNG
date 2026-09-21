/*
 * RenderSize.cs - panel-vs-render viewport size decoupling for the 2x
 * supersample readback. Navigation math (cursor ray unprojection, pans, zooms)
 * must stay in on-screen panel px while the engine renders into a
 * supersample-scaled render target; this is the single authoritative mapping.
 */

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>Resolved viewport sizes for one tick: panel px (what the user sees
/// and points at) and render-target px (what the engine rasterizes).</summary>
public readonly record struct RenderSize(uint PanelW, uint PanelH, uint RtW, uint RtH);

/// <summary>Computes the panel and render-target pixel sizes.</summary>
public static class RenderSizing
{
    /// <summary>Supersample multiplier applied to the render target when the
    /// Supersample setting is on. A 1px line becomes 2px of SMAA footprint.</summary>
    public const int SupersampleFactor = 2;

    /// <summary>Panel px stay as given; the render target scales by
    /// <see cref="SupersampleFactor"/> when <paramref name="supersample"/>.</summary>
    public static RenderSize Resolve(uint panelW, uint panelH, bool supersample)
    {
        var w = Math.Max(1u, panelW);
        var h = Math.Max(1u, panelH);
        return supersample
            ? new RenderSize(w, h, w * SupersampleFactor, h * SupersampleFactor)
            : new RenderSize(w, h, w, h);
    }
}