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

public class ProgramViewModel : ViewModelBase
{
    private readonly IGrblService? _grbl;
    private ProgramStreamSession? _session;
    private GCodeProgram? _program;
    private MachineState _machineState = new();
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

    public ObservableCollection<GCodeProgramRow> ProgramRows { get; } = new();

    public ObservableCollection<ProgramLimitRow> LimitRows { get; } = new();

    public bool HasLimits => LimitRows.Count > 0;

    /// <summary>Raw program text as loaded (transforms such as height map/rotation rewrite it).</summary>
    public string? LoadedText { get; private set; }

    // Raised after any row status/progress change — the status bar's block
    // counter and any other observers re-read their state on it.
    public event Action? RowsChanged;

    // Raised while streaming when a row is touched — the view scrolls the send
    // frontier into the middle of the program pane so the queued commands stay
    // visible. The payload is the touched row's index in <see cref="ProgramRows"/>
    // (rows are uniform height, so the index maps to an exact scroll offset).
    public event Action<int>? ScrollRequested;

    public int DoneCount => ProgramRows.Count(r => r.Status == "\u2713");

    /// <summary>Sendable lines in the loaded program (comments excluded) — the run's block total.</summary>
    public int SendableCount { get; private set; }

    public string ProgressText => $"{DoneCount}/{SendableCount}";

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

    /// <summary>Loads an engine-parsed program (the Open File path): rows keep real file line numbers, limits come from the single parse pass.</summary>
    public void LoadProgram(GCodeProgram program, string? text)
    {
        _program = program;
        LoadedText = text;
        ProgramRows.Clear();
        LimitRows.Clear();
        SendableCount = program.Entries.Count(e => e.IsSendable);
        OnPropertyChanged(nameof(HasLimits));
        OnPropertyChanged(nameof(SendableCount));
        OnPropertyChanged(nameof(ProgressText));

        foreach (var entry in program.Entries)
            ProgramRows.Add(new GCodeProgramRow(entry.FileLine, "\u00b7", entry.Text, null));

        RefreshLimits();
        RowsChanged?.Invoke();
    }

    /// <summary>Unloads the loaded program (rows, limits, loaded text) — used
    /// when the active machine profile changes, since a different geometry
    /// invalidates limits and jog semantics.</summary>
    public void ClearProgram()
    {
        _program = null;
        LoadedText = null;
        ProgramRows.Clear();
        LimitRows.Clear();
        SendableCount = 0;
        OnPropertyChanged(nameof(HasLimits));
        OnPropertyChanged(nameof(SendableCount));
        OnPropertyChanged(nameof(ProgressText));
        RowsChanged?.Invoke();
    }

    private void RefreshLimits()
    {
        LimitRows.Clear();
        if (_program is null)
            return;

        var limits = GCodeLimitsCalculator.Compute(_program.Tokens);
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

    /// <summary>Reset the loaded program to the start: all rows return to the un-sent state and the view scrolls to the first line.</summary>
    public void Rewind()
    {
        for (var i = 0; i < ProgramRows.Count; i++)
            ProgramRows[i] = ProgramRows[i] with { Status = "\u00b7", Detail = null };
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
    /// Aborts the run: stops sending immediately and feed-holds if the machine
    /// is running so motion halts. The machine is left in Hold — use the cycle
    /// bar's Stop (Reset) while Held to fully reset the controller.
    /// </summary>
    public async Task StopAsync()
    {
        var session = _session;
        if (session is null)
            return;
        session.Stop();
        if (IsMachineState("Run"))
        {
            try { _grbl?.SendRealtime(GrblRealtime.FeedHold); } catch (InvalidOperationException) { }
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
        ProgramRows[index] = update.Status switch
        {
            ProgramLineStatus.Sent => row with { Status = "@" },
            ProgramLineStatus.Ok => row with { Status = "\u2713", Detail = null },
            _ => row with { Status = "\u2715", Detail = update.Detail },
        };
        OnPropertyChanged(nameof(ProgressText));
        RowsChanged?.Invoke();
        ScrollRequested?.Invoke(index);
    }

    private void Finalize(ProgramRunResult result)
    {
        // Lines still in flight never resolved; return them to idle.
        for (var i = 0; i < ProgramRows.Count; i++)
        {
            if (ProgramRows[i].Status == "@")
                ProgramRows[i] = ProgramRows[i] with { Status = "\u00b7" };
        }

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