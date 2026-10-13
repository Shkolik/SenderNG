/*
 * RebuildGate.cs - coalescing gate that collapses bursts of run-state
 * mutations into a bounded refresh cadence
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

namespace SenderNG.Modules.Common.Render;

/// <summary>Coalescing gate for the swap-pump: a fresh STATIC geometry is
/// rebuilt + re-uploaded (a full-toolpath O(n) copy plus an engine device
/// upload/dispose) whenever any run-state mutation is applied. Rebuilding on
/// every run-state tick is the measured cost driver on large toolpaths, so the
/// gate collapses bursts (repeat mutations arrive far faster than pixels absorb
/// them) into a bounded refresh cadence while still flushing immediately when a
/// large change storm — e.g. a full program load — queues a lot of mutations.</summary>
public sealed class RebuildGate
{
    /// <summary>Minimum wall time between two rebuilds (keeps steady-state
    /// coloring smooth while cutting per-tick rebuild cost on large toolpaths).</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Pending mutations that force an immediate rebuild even inside
    /// the minimum interval (application/burst paths like program load).</summary>
    public int FlushThreshold { get; init; } = 1000;

    private DateTime _lastRebuildUtc = DateTime.MinValue;
    private int _pendingMutations;
    private bool _dirty;

    /// <summary>Pending mutations accumulated since the last granted rebuild
    /// (read seam for "marked, not GPU-touched" assertions).</summary>
    public int PendingMutations => _pendingMutations;

    /// <summary>Record one pooling mutation (add/color/overlay).</summary>
    public void MarkDirty()
    {
        _dirty = true;
        _pendingMutations++;
    }

    /// <summary>Whether a rebuild should run now. No rebuild while clean (nothing
    /// changed since the last pump). When dirty, a rebuild is granted either on
    /// burst flush (<see cref="FlushThreshold"/>) or once <see cref="MinInterval"/>
    /// elapsed — continuing to defer keeps <see cref="Pump"/> from rescanning until
    /// either condition holds, so an active run rebuilds at most once per interval
    /// instead of once per run-state tick. A grant consumes the accumulated
    /// mutations and resets the interval.</summary>
    public bool RebuildDue(DateTime nowUtc)
    {
        if (!_dirty || (_pendingMutations < FlushThreshold && nowUtc - _lastRebuildUtc < MinInterval))
            return false;
        _dirty = false;
        _pendingMutations = 0;
        _lastRebuildUtc = nowUtc;
        return true;
    }
}