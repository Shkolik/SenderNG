/*
 * ProgramRunPlan.cs - the entry range one program session streams
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace CNC.Core.Services;

/// <summary>
/// The ordered slice of a loaded program one session streams — the whole file,
/// a tail from a row, or an arbitrary row selection. Entries keep their original
/// <see cref="GCodeProgramEntry.Sequence"/>, so per-line progress still maps to
/// the program grid's row index (<c>Sequence - 1</c>).
/// <para>
/// A plan carries entries, not a start/end range: a selection may be
/// non-contiguous, and the streamer's own rules (skip non-sendable lines, stop
/// at a program-end marker) must see the same entries a full run would. The
/// <c>%</c> demarcation line is therefore kept in a plan even though it is never
/// sent — dropping it would drop the file's end marker.
/// </para>
/// </summary>
public sealed record ProgramRunPlan
{
    public IReadOnlyList<GCodeProgramEntry> Entries { get; init; } = [];

    /// <summary>Every entry of the program (the ordinary Run).</summary>
    public static ProgramRunPlan Whole(GCodeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new ProgramRunPlan { Entries = program.Entries };
    }

    /// <summary>Rows <paramref name="row"/>..end — the "run from here" plan.
    /// An out-of-range row yields an empty plan (callers gate on a selection).</summary>
    public static ProgramRunPlan FromLine(GCodeProgram program, int row)
    {
        ArgumentNullException.ThrowIfNull(program);
        var entries = program.Entries;
        if (row < 0 || row >= entries.Count)
            return new ProgramRunPlan();
        if (row == 0)
            return Whole(program);
        return new ProgramRunPlan { Entries = [.. entries.Skip(row)] };
    }

    /// <summary>The picked rows in file order — the "send selected lines" plan.
    /// Duplicates and out-of-range indices are dropped; the result is a selection,
    /// not a span, so non-contiguous picks stay non-contiguous.</summary>
    public static ProgramRunPlan Selection(GCodeProgram program, IReadOnlyList<int> rows)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(rows);
        var entries = program.Entries;
        var picked = rows
            .Where(i => i >= 0 && i < entries.Count)
            .Distinct()
            .OrderBy(i => i)
            .Select(i => entries[i])
            .ToArray();
        return new ProgramRunPlan { Entries = picked };
    }
}
