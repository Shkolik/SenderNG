using System;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>Coalescing gate for the swap-pump: a fresh STATIC geometry is
/// rebuilt + re-uploaded (a full-toolpath O(n) copy plus an engine device
/// upload/dispose) whenever any run-state mutation is applied. Rebuilding on
/// every run-state tick is the measured cost driver on large toolpaths, so the
/// gate collapses bursts (repeat mutations arrive far faster than pixels absorb
/// them) into a bounded refresh cadence while still flushing immediately when a
/// large change storm — e.g. a full program load — queues a lot of mutations.</summary>
internal sealed class RebuildGate
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