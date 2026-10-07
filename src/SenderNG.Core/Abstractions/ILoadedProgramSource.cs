/*
 * ILoadedProgramSource.cs - Read-only program surface for content observers
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

namespace CNC.Core.Abstractions;

/// <summary>Send state of one program row (the program view's per-line status).</summary>
public enum ProgramRowState
{
    /// <summary>Not sent yet (the program view's "·").</summary>
    Idle,

    /// <summary>Sent, awaiting the controller's reply (the program view's "@").</summary>
    Sent,

    /// <summary>Acknowledged ok (the program view's "✓").</summary>
    Ok,

    /// <summary>Rejected with an error (the program view's "✕").</summary>
    Error,
}

/// <summary>
/// Read-only program surface for content observers that live outside the app
/// assembly (plotter plugins): the parsed program plus the run frontier, enough
/// to rebuild toolpath geometry and follow a run without touching UI types.
/// Implemented by the app's ProgramViewModel and registered in DI alongside it
/// (the plugin resolves this interface, never the app's view-model type).
/// </summary>
public interface ILoadedProgramSource
{
    /// <summary>The engine-parsed program behind the rows (null when nothing is loaded).
    /// Rows are index-aligned with <see cref="CNC.Core.Services.GCodeProgram.Entries"/>.</summary>
    CNC.Core.Services.GCodeProgram? Program { get; }

    /// <summary>The loaded program was replaced or cleared — content observers
    /// rebuild their geometry from <see cref="Program"/> on it. Raised by both
    /// the Open flow and program transforms (they all reload through the same
    /// path), so a transformed program is picked up for free.</summary>
    // System.Action qualified: kept fully qualified throughout for clarity and
    // consistency with the rest of the event surface.
    event System.Action? ProgramChanged;

    /// <summary>A row status changed (send frontier moved, ok/error arrived) —
    /// no geometry rebuild needed, only progress redraw. Fires per line during
    /// a run; observers throttle.</summary>
    event System.Action? RowsChanged;

    /// <summary>Row count (index-aligned with <see cref="CNC.Core.Services.GCodeProgram.Entries"/>
    /// while a program is loaded).</summary>
    int RowCount { get; }

    /// <summary>Sendable (streamable) blocks of the loaded program — the run's
    /// total. Implementations must keep this O(1): observers re-read it on
    /// every frontier tick.</summary>
    int SendableCount { get; }

    /// <summary>Acknowledged-ok sendable blocks of the loaded program — the
    /// run's done count. Implementations must keep this O(1) (incrementally
    /// tracked): observers re-read it on every frontier tick, and a per-tick
    /// scan of the whole program is O(n²) over a run — the plotter header
    /// froze the UI thread for tens of seconds on large programs before the
    /// counters were cached.</summary>
    int DoneCount { get; }

    /// <summary>Send state of row <paramref name="index"/> (0-based, rows order).</summary>
    ProgramRowState GetRowState(int index);

    /// <summary>Highest row index whose send state has changed (acked, sent or
    /// errored) — the run frontier as a row index, O(1). Resets to 0 on
    /// load/clear/rewind. Observers use it to bound their recolor windows
    /// without scanning the program (comments rows stay idle forever, so a
    /// scan can never stop at the first idle row).</summary>
    int TouchedRow { get; }
}
