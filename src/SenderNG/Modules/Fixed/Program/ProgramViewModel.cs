/*
 * ProgramViewModel.cs - Program view model
 */

/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Localization;
using SenderNG.ViewModels;
using SenderNG;

namespace SenderNG.Modules.Fixed;

// Status glyphs: "·" idle, "@" in flight (sent, awaiting reply), "✓" ok, "✕" error.
public record GCodeProgramRow(int Line, string Status, string Data, string? Detail);

public record ProgramLimitRow(string Letter, double Min, double Max);

/// <summary>
/// ObservableCollection with bulk-update suppression: mutations between
/// <see cref="BeginUpdate"/> and <see cref="EndUpdate"/> raise no per-item
/// CollectionChanged events; <see cref="EndUpdate"/> raises one Reset. The
/// program list binds to it, so loading/rewinding a 100k-line file no longer
/// forces layout work per row on the UI thread.
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    private int _batchDepth;

    public void BeginUpdate() => _batchDepth++;

    public void EndUpdate()
    {
        if (_batchDepth == 0)
            throw new InvalidOperationException("EndUpdate without BeginUpdate");
        if (--_batchDepth == 0)
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_batchDepth == 0)
            base.OnCollectionChanged(e);
    }
}

// ILoadedProgramSource: the read-only program surface external plugins (the
// plotter) resolve from DI — they must not reference this app-assembly type.
public class ProgramViewModel : ViewModelBase, ILoadedProgramSource, IProgramTransformTarget
{
    private readonly IGrblService? _grbl;
    private ProgramStreamSession? _session;
    private GCodeProgram? _program;
    private IReadOnlyList<AxisLimits> _limits = [];
    private MachineState _machineState = new();
    // Incremental acked count (O(1) reads): a per-read scan of ProgramRows is
    // O(n) and ProgressText/DoneCount are re-read on EVERY row update, so a
    // scan made a run O(n²) — large programs froze the UI thread for seconds
    // (the plotter header re-read these counters per frontier tick).
    private int _doneCount;
    private IDisposable? _stateSubscription;
    private IDisposable? _workParametersSubscription;

    public ProgramViewModel(IGrblService? grbl = null)
    {
        _grbl = grbl;
        RunCommand = new DelegateCommand(_ => _ = RunAsync(), _ => CanRun);
        PauseCommand = new DelegateCommand(_ => Pause(), _ => IsRunning && IsMachineState("Run"));
        ResumeCommand = new DelegateCommand(_ => Resume(), _ => IsRunning && IsMachineState("Hold"));
        StopCommand = new DelegateCommand(_ => _ = StopAsync(), _ => IsRunning);

        if (_grbl is not null)
        {
            _stateSubscription = _grbl.State.Subscribe(state => UiThread.Post(() =>
            {
                _machineState = state;
                OnPropertyChanged(nameof(IsPaused));
                RaiseCommandStates();
            }));

            // Work parameters seed the emulator behind the limits computation:
            // a program loaded before connecting was computed with empty tables
            // (zero offsets, no G28/G30 rows), so recompute when controller data
            // arrives — and whenever offsets change thereafter, keeping G53/
            // G28/G30-driven limits honest.
            _workParametersSubscription = _grbl.WorkParametersChanges.Subscribe(wp => UiThread.Post(() =>
            {
                if (_program is null || LimitRows.Count == 0)
                    return;
                RefreshLimits();
            }));
        }
    }

    /// <summary>Rows with batched change notifications: a big file's load/rewind
    /// adds tens of thousands of items, and an ObservableCollection raises a
    /// CollectionChanged per item — the program list (and every observer) did
    /// layout work per row and the UI froze for seconds. Load/rewind/finalize
    /// wrap their bulk mutations in Begin/EndUpdate; per-line run updates stay
    /// unbatched (the list must track the live frontier row by row).</summary>
    public BatchObservableCollection<GCodeProgramRow> ProgramRows { get; } = new();

    public ObservableCollection<ProgramLimitRow> LimitRows { get; } = new();

    public bool HasLimits => LimitRows.Count > 0;

    /// <summary>Raw program text as loaded (transforms such as height map/rotation rewrite it).</summary>
    public string? LoadedText { get; private set; }

    // ---- ILoadedProgramSource: the plugin-facing program surface ----

    /// <summary>The engine-parsed program behind the rows (null when nothing is loaded).</summary>
    public GCodeProgram? Program => _program;

    /// <summary>The loaded program was replaced or cleared — content observers
    /// (the plotter) rebuild their geometry from <see cref="Program"/> on it.
    /// Raised by both the Open flow and the transforms (they all reload through
    /// <see cref="LoadProgram"/>, so a transformed program is picked up for free).</summary>
    public event Action? ProgramChanged;

    /// <summary>Row count (index-aligned with <see cref="GCodeProgram.Entries"/>).</summary>
    int ILoadedProgramSource.RowCount => ProgramRows.Count;

    ProgramRowState ILoadedProgramSource.GetRowState(int index)
    {
        if (index < 0 || index >= ProgramRows.Count)
            return ProgramRowState.Idle;
        return ProgramRows[index].Status switch
        {
            "@" => ProgramRowState.Sent,
            "\u2713" => ProgramRowState.Ok,
            "\u2715" => ProgramRowState.Error,
            _ => ProgramRowState.Idle,
        };
    }

    int ILoadedProgramSource.SendableCount => SendableCount;

    int ILoadedProgramSource.DoneCount => DoneCount;

    /// <summary>Highest row index whose send state has changed (run frontier as
    /// a row index) — maintained incrementally in <see cref="ApplyUpdate"/>,
    /// reset on load/clear/rewind. O(1) reads (observers bound their recolor
    /// windows with it instead of scanning rows).</summary>
    public int TouchedRow { get; private set; }

    // Raised after any row status/progress change — the status bar's block
    // counter and any other observers re-read their state on it.
    public event Action? RowsChanged;

    // ---- IProgramTransformTarget: the plugin-facing transform surface ----

    /// <summary>Raw program text as loaded (transforms such as height
    /// map/rotation rewrite it) — exposed through <see cref="IProgramTransformTarget"/>
    /// so plugin transforms can read and re-load the program without touching
    /// the app's view-model type.</summary>
    string? IProgramTransformTarget.LoadedText => LoadedText;

    /// <summary>Per-axis program limits (bounding box), as <see cref="AxisLimits"/>
    /// — the transform pivot resolves its corners/center from these. Kept in
    /// sync by <see cref="ApplyLimits"/> / <see cref="ClearProgram"/>.</summary>
    IReadOnlyList<AxisLimits> IProgramTransformTarget.Limits => _limits;

    void IProgramTransformTarget.LoadProgram(GCodeProgram program, string? text)
        => LoadProgram(program, text);

    // Raised while streaming when a row is touched — the view scrolls the send
    // frontier into the middle of the program pane so the queued commands stay
    // visible. The payload is the touched row's index in <see cref="ProgramRows"/>
    // (rows are uniform height, so the index maps to an exact scroll offset).
    public event Action<int>? ScrollRequested;

    /// <summary>Acknowledged sendable rows — O(1) (incrementally tracked in
    /// <see cref="ApplyUpdate"/>; resets on load/clear/rewind).</summary>
    public int DoneCount => _doneCount;

    /// <summary>Sendable lines in the loaded program (comments excluded) — the run's block total.</summary>
    public int SendableCount { get; private set; }

    public string ProgressText => $"{DoneCount}/{SendableCount}";

    /// <summary>True while a program is being parsed/analyzed off the UI thread
    /// (the open-file flow) — the program view shows a loading overlay.</summary>
    public bool IsBusy { get; private set; }

    public bool IsRunning => _session is not null;

    public bool IsPaused => IsRunning && IsMachineState("Hold");

    /// <summary>Last run summary (started / finished / failed / stopped), surfaces in the status bar.</summary>
    public string? RunMessage { get; private set; }

    public ICommand RunCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand { get; }

    private bool CanRun => _grbl?.IsConnected == true && !IsRunning && ProgramRows.Count > 0 && !IsMachineState("Alarm");

    private bool IsMachineState(string value) =>
        _machineState.Status.StartsWith(value, StringComparison.OrdinalIgnoreCase);

    private void RaiseCommandStates()
    {
        (RunCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (PauseCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (ResumeCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (StopCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(ProgressText));
    }

    /// <summary>Loads an engine-parsed program (the Open File path): rows keep
    /// real file line numbers, limits come from the single parse pass. The
    /// limits may arrive precomputed (the app computes them off the UI thread —
    /// the emulator pass is seconds on big files); null falls back to the
    /// inline computation (tests, transforms).</summary>
    public void LoadProgram(GCodeProgram program, string? text, IReadOnlyList<AxisLimits>? limits = null)
    {
        _program = program;
        LoadedText = text;
        _doneCount = 0;
        TouchedRow = 0;
        SendableCount = program.Entries.Count(e => e.IsSendable);
        OnPropertyChanged(nameof(HasLimits));
        OnPropertyChanged(nameof(SendableCount));
        OnPropertyChanged(nameof(ProgressText));

        ProgramRows.BeginUpdate();
        ProgramRows.Clear();
        foreach (var entry in program.Entries)
            ProgramRows.Add(new GCodeProgramRow(entry.FileLine, "\u00b7", entry.Text, null));
        ProgramRows.EndUpdate();

        if (limits is not null)
            ApplyLimits(limits);
        else
            RefreshLimits();
        ProgramChanged?.Invoke();
        RowsChanged?.Invoke();
    }

    /// <summary>Rows from a precomputed limits pass (single emulator run, off the UI thread).</summary>
    private void ApplyLimits(IReadOnlyList<AxisLimits> limits)
    {
        _limits = limits ?? [];
        LimitRows.Clear();
        if (limits is not null)
        {
            foreach (var l in limits)
                LimitRows.Add(new ProgramLimitRow(l.Letter.ToString(), l.Min, l.Max));
        }
        OnPropertyChanged(nameof(HasLimits));
    }

    /// <summary>Unloads the loaded program (rows, limits, loaded text) — used
    /// when the active machine profile changes, since a different geometry
    /// invalidates limits and jog semantics.</summary>
    public void ClearProgram()
    {
        _program = null;
        LoadedText = null;
        _limits = [];
        ProgramRows.Clear();
        LimitRows.Clear();
        _doneCount = 0;
        TouchedRow = 0;
        SendableCount = 0;
        OnPropertyChanged(nameof(HasLimits));
        OnPropertyChanged(nameof(SendableCount));
        OnPropertyChanged(nameof(ProgressText));
        ProgramChanged?.Invoke();
        RowsChanged?.Invoke();
    }

    private void RefreshLimits()
    {
        LimitRows.Clear();
        if (_program is null)
        {
            _limits = [];
            return;
        }

        var limits = GCodeLimitsCalculator.Compute(_program.Tokens);
        _limits = limits ?? [];
        if (limits is not null)
        {
            foreach (var l in limits)
                LimitRows.Add(new ProgramLimitRow(l.Letter.ToString(), l.Min, l.Max));
        }
        OnPropertyChanged(nameof(HasLimits));
    }

    /// <summary>Load from raw text (probe transforms, tests): parsed by the same engine gate, rows from the entries.</summary>
    public void LoadGCode(string? text) =>
        LoadProgram(GCodeProgram.Parse(text), text);

    /// <summary>Marks the program busy while the open-file flow parses and
    /// analyzes it off the UI thread (the view shows a loading overlay).</summary>
    public void SetBusy(bool busy)
    {
        if (IsBusy == busy)
            return;
        IsBusy = busy;
        OnPropertyChanged(nameof(IsBusy));
    }

    /// <summary>Reset the loaded program to the start: all rows return to the un-sent state and the view scrolls to the first line.</summary>
    public void Rewind()
    {
        ProgramRows.BeginUpdate();
        for (var i = 0; i < ProgramRows.Count; i++)
            ProgramRows[i] = ProgramRows[i] with { Status = "\u00b7", Detail = null };
        ProgramRows.EndUpdate();
        _doneCount = 0;
        TouchedRow = 0;
        RowsChanged?.Invoke();
        ScrollRequested?.Invoke(0);
    }

    /// <summary>
    /// Starts streaming the loaded program: rewinds rows, starts the cycle
    /// (realtime cycle-start byte), then sends lines awaiting each "ok".
    /// </summary>
    public async Task RunAsync()
    {
        if (_grbl is null || !_grbl.IsConnected || IsRunning || _program is null || ProgramRows.Count == 0)
            return;

        Rewind();
        var streamer = new GCodeProgramStreamer(_grbl);
        // Direct IProgress (NOT Progress<T>): callbacks from the run loop are
        // marshalled via UiThread — inline when no dispatcher (deterministic
        // unit tests), enqueued before the completion signal on the UI thread
        // (so the dispatcher drains them before Finalize re-raises state).
        _session = streamer.Start(_program, new UiProgress(ApplyUpdate));
        RunMessage = LocText.Format("module.program.started", "Run started ({0} blocks)", SendableCount);
        OnPropertyChanged(nameof(RunMessage));
        RaiseCommandStates();

        try
        {
            _grbl.SendRealtime(GrblRealtime.CycleStart);
        }
        catch (InvalidOperationException)
        {
            // Not connected between the checks and the byte write; the run will
            // fail on the first line anyway.
        }

        var result = await _session.Completion;
        _session = null;
        Finalize(result);
    }

    /// <summary>Feed-hold pause: halts motion; the send loop observes Hold and waits.</summary>
    public void Pause()
    {
        if (!IsRunning || !IsMachineState("Run"))
            return;
        try { _grbl?.SendRealtime(GrblRealtime.FeedHold); } catch (InvalidOperationException) { }
    }

    /// <summary>Resumes a paused run (cycle start while the machine is Held).</summary>
    public void Resume()
    {
        if (!IsRunning || !IsMachineState("Hold"))
            return;
        try { _grbl?.SendRealtime(GrblRealtime.CycleStart); } catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Aborts the run: stops sending immediately and halts motion with a controlled
    /// stop. grblHAL gets the 0x19 controlled stop (lands in Hold, no alarm); classic
    /// grbl has no 0x19, so it feed-holds and the user's cycle-bar Stop resets.
    /// </summary>
    public async Task StopAsync()
    {
        var session = _session;
        if (session is null)
            return;
        session.Stop();
        if (IsMachineState("Run"))
        {
            var code = GrblRealtime.ProgramStopByte(_grbl?.Capabilities.IsGrblHAL == true);
            try { _grbl?.SendRealtime(code); } catch (InvalidOperationException) { }
        }
        await session.Completion; // Finalize runs in RunAsync's continuation
    }

    /// <summary>Cycle bar sent a hard reset (Stop while Held): end the session without resuming.</summary>
    public void OnHardReset()
    {
        _session?.Stop();
    }

    private sealed class UiProgress : IProgress<ProgramLineUpdate>
    {
        private readonly Action<ProgramLineUpdate> _apply;
        public UiProgress(Action<ProgramLineUpdate> apply) => _apply = apply;
        public void Report(ProgramLineUpdate value) => UiThread.Post(() => _apply(value));
    }

    private void ApplyUpdate(ProgramLineUpdate update)
    {
        var index = update.Sequence - 1;
        if (index < 0 || index >= ProgramRows.Count)
            return;

        var row = ProgramRows[index];
        if (row.Status == "\u2713")
            _doneCount--; // a re-sent line loses its previous ack
        var updated = update.Status switch
        {
            ProgramLineStatus.Sent => row with { Status = "@" },
            ProgramLineStatus.Ok => row with { Status = "\u2713", Detail = null },
            _ => row with { Status = "\u2715", Detail = update.Detail },
        };
        if (updated.Status == "\u2713")
            _doneCount++;
        if (index > TouchedRow)
            TouchedRow = index;
        ProgramRows[index] = updated;
        OnPropertyChanged(nameof(ProgressText));
        RowsChanged?.Invoke();
        ScrollRequested?.Invoke(index);
    }

    private void Finalize(ProgramRunResult result)
    {
        // Lines still in flight never resolved; return them to idle.
        ProgramRows.BeginUpdate();
        for (var i = 0; i < ProgramRows.Count; i++)
        {
            if (ProgramRows[i].Status == "@")
                ProgramRows[i] = ProgramRows[i] with { Status = "\u00b7" };
        }
        ProgramRows.EndUpdate();

        RunMessage = result.Outcome switch
        {
            ProgramRunOutcome.Completed => LocText.Format("module.program.finished", "Run finished ({0} blocks)", result.LinesOk),
            ProgramRunOutcome.Failed => LocText.Format("module.program.failed", "Run failed: {0}", result.Error),
            _ => LocText.Resolve("module.program.stopped", "Run stopped"),
        };
        OnPropertyChanged(nameof(RunMessage));
        RaiseCommandStates();
        RowsChanged?.Invoke();
    }
}