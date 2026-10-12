/*
 * GlLineWidths.cs - the line-width acceptance probe, kept as per-context
 * evidence/logging for the due-diligence doc (widths render as quads now)
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

using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;

namespace SenderNG.Modules.Plotter.GL.Rendering;

/// <summary>The checkpoint-3 line-width evidence, shared: a core driver
/// answered <c>glLineWidth(w &gt; 1.0)</c> with <c>GL_INVALID_VALUE</c> while
/// <c>ALIASED_LINE_WIDTH_RANGE</c> reported <c>[1, 255]</c> — the reported
/// range is a promise the driver does not keep, so the widths had to be
/// tried. Since checkpoint 4 the widths render as screen-space quads
/// (<see cref="GlShaders.LineGeometry"/>) and never read driver acceptance
/// back: the probe stays on as per-context EVIDENCE, logged once by the
/// scene constructor for the due-diligence doc (checkpoint-4 item 4).</summary>
internal static class GlLineWidths
{
    /// <summary>The probe order: the toolpath thickness (3.2), the plan-era
    /// chrome width (1.5), plus the integers checkpoint 3 left unverified.</summary>
    internal static readonly float[] Candidates = [1f, 1.5f, 2f, 3f, 3.2f];

    /// <summary>The only width every driver accepts — the documented floor
    /// of the evidence set.</summary>
    internal const float Fallback = 1f;

    /// <summary>Try every candidate and return the widths the driver accepted,
    /// ascending. The error queue is drained before each trial (a stale error
    /// from unrelated work must not condemn a width) and after it (the trial's
    /// own error must not condemn the next one). Evidence only — no draw path
    /// picks a width from the result.</summary>
    internal static float[] Probe(GlApi gl)
    {
        var accepted = new List<float>(Candidates.Length);
        Drain(gl);
        foreach (var w in Candidates)
        {
            gl.LineWidth(w);
            if (gl.GetError() == GLEnum.NoError)
                accepted.Add(w);
            Drain(gl);
        }
        return accepted.ToArray();
    }

    /// <summary>Consume the pending <c>glGetError</c> queue, bounded — the
    /// queue empties to <c>GL_NO_ERROR</c>, the bound only guards a broken
    /// context from spinning.</summary>
    internal static void Drain(GlApi gl)
    {
        for (var i = 0; i < 16 && gl.GetError() != GLEnum.NoError; i++)
        {
        }
    }
}
