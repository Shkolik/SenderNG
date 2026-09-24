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

using CNC.Core.Services;

namespace CNC.Core.Abstractions;

/// <summary>
/// Read/write program surface for plugin transforms that rewrite the loaded
/// program (probing's height-map/rotation apply): the raw text plus a write
/// path that reloads a transformed program through the app's engine-gated
/// loader (parse → check errors → transform → re-parse → load). Implemented by
/// the app's ProgramViewModel and registered in DI alongside
/// <see cref="ILoadedProgramSource"/> — plugins resolve this interface, never
/// the app's view-model type.
/// </summary>
public interface IProgramTransformTarget
{
    /// <summary>Raw program text as loaded (null when nothing is loaded).</summary>
    string? LoadedText { get; }

    /// <summary>Per-axis limits (bounding box) of the loaded program; the
    /// transform pivot resolves its corners/center from these.</summary>
    IReadOnlyList<AxisLimits> Limits { get; }

    /// <summary>Loads a transformed program through the same engine-gated path
    /// as File → Transform. <paramref name="program"/> must already parse
    /// cleanly (the caller runs the enforce-parse-error gate before calling).</summary>
    void LoadProgram(GCodeProgram program, string? text);
}