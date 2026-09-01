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

using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>Per-line streaming status. Sent = in flight (executing head), Ok/Error = resolved.</summary>
public enum ProgramLineStatus
{
    Sent,
    Ok,
    Error,
}

/// <summary>One per-line progress notification, keyed by <see cref="GCodeProgramEntry.Sequence"/>.</summary>
public sealed record ProgramLineUpdate(int Sequence, ProgramLineStatus Status, string? Detail);

/// <summary>How a program run ended.</summary>
public enum ProgramRunOutcome
{
    /// <summary>Every line was accepted (or the program end was reached) without stop/alarm.</summary>
    Completed,
    /// <summary>The controller rejected a line, alarmed, or the connection dropped.</summary>
    Failed,
    /// <summary>The user requested a stop.</summary>
    Stopped,
}

public sealed record ProgramRunResult(ProgramRunOutcome Outcome, string? Error, int LinesSent, int LinesOk)
{
    public bool IsSuccess => Outcome == ProgramRunOutcome.Completed;
}

/// <summary>
/// Control handle for one running program: <see cref="Stop"/> aborts the run
/// (cancels the pending line, sends nothing further); completion is observed
/// via <see cref="Completion"/>. Pause/resume are NOT session operations — they
/// are controller operations (feed hold / cycle start) that the session picks
/// up from the machine state: the send loop pauses while the controller is in
/// Hold and resumes when it returns to Run/Idle.
/// </summary>
public sealed class ProgramStreamSession
{
    private readonly CancellationTokenSource _cts = new();
    private int _stopRequested;

    internal ProgramStreamSession() { }

    internal CancellationToken Token => _cts.Token;

    /// <summary>Completes when the run ends; carries the outcome.</summary>
    public Task<ProgramRunResult> Completion { get; internal set; } = Task.FromResult(
        new ProgramRunResult(ProgramRunOutcome.Failed, "Not started", 0, 0));

    /// <summary>Aborts the run: cancels the in-flight line, stops the send loop.</summary>
    public void Stop()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        _cts.Cancel();
    }

    internal bool IsStopRequested => Volatile.Read(ref _stopRequested) != 0;

    /// <summary>Internal: alarm/disconnect observed by the run loop cancels the pending wait.</summary>
    internal void Cancel() => _cts.Cancel();
}

/// <summary>
/// Streams a <see cref="GCodeProgram"/> to the controller, one line at a time,
/// awaiting each line's "ok" before the next (the service's streaming send
/// keeps status polling active). The machine state drives the loop:
/// <list type="bullet">
/// <item><b>Hold</b> (feed hold) pauses sending until the operator resumes (cycle start);</item>
/// <item><b>Alarm</b> aborts the run immediately, cancelling any in-flight line;</item>
/// <item>disconnect aborts with a clear error.</item>
/// </list>
/// A controller rejection ("error:NN") marks the line and FAILS the run — no
/// skipping (the file was validated at load; a send-time rejection means the
/// machine disagrees, so stopping is the honest response).
/// </summary>
public sealed class GCodeProgramStreamer
{
    private readonly IGrblService _grbl;

    public GCodeProgramStreamer(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
    }

    /// <summary>
    /// Starts a run. A program carrying parse errors FAILS IMMEDIATELY without
    /// sending a single byte — the load gate rejects such files, so reaching the
    /// streamer with errors is a programming error, and a silently-shrunk
    /// program is never acceptable (a partial program is a different program).
    /// </summary>
    public ProgramStreamSession Start(GCodeProgram program, IProgress<ProgramLineUpdate>? progress = null)
    {
        var session = new ProgramStreamSession();
        if (program.HasErrors)
        {
            session.Completion = Task.FromResult(new ProgramRunResult(
                ProgramRunOutcome.Failed, program.Errors[0], 0, 0));
            return session;
        }
        session.Completion = Task.Run(() => RunAsync(program, progress, session));
        return session;
    }

    private static bool IsHold(string? status) =>
        status?.StartsWith("Hold", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAlarm(string? status) =>
        status?.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<ProgramRunResult> RunAsync(
        GCodeProgram program,
        IProgress<ProgramLineUpdate>? progress,
        ProgramStreamSession session)
    {
        var ct = session.Token;
        var latest = new MachineState { Status = string.Empty };
        int linesSent = 0, linesOk = 0;

        // The subscription seeds `latest` from the replaying BehaviorSubject and
        // keeps it fresh; an Alarm cancels the pending await immediately (a real
        // controller flushes its buffer into Alarm and may never reply — the
        // cancellation pulls the in-flight SendProgramLineAsync out of its wait).
        using var stateSub = _grbl.State.Subscribe(s =>
        {
            latest = s;
            if (IsAlarm(s.Status))
                session.Cancel();
        });

        try
        {
            if (!_grbl.IsConnected)
                return new ProgramRunResult(ProgramRunOutcome.Failed, "Not connected.", 0, 0);

            foreach (var entry in program.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (session.IsStopRequested)
                    return new ProgramRunResult(ProgramRunOutcome.Stopped, "Stopped.", linesSent, linesOk);

                // Pause gate: feed hold parks the controller in Hold; stay here
                // until the operator resumes (cycle start) or stops the run.
                while (IsHold(latest.Status))
                {
                    ct.ThrowIfCancellationRequested();
                    if (session.IsStopRequested)
                        return new ProgramRunResult(ProgramRunOutcome.Stopped, "Stopped.", linesSent, linesOk);
                    await Task.Delay(50, ct);
                }

                if (IsAlarm(latest.Status))
                    return new ProgramRunResult(ProgramRunOutcome.Failed, $"Alarm: {latest.Status}", linesSent, linesOk);

                if (!entry.IsSendable)
                {
                    // Percent demarcation is a file marker, not a command; the
                    // closing % also ends the program.
                    if (entry.IsProgramEnd)
                        break;
                    continue;
                }

                progress?.Report(new ProgramLineUpdate(entry.Sequence, ProgramLineStatus.Sent, null));
                linesSent++;

                string reply;
                try
                {
                    reply = await _grbl.SendProgramLineAsync(entry.Text, ct);
                }
                catch (OperationCanceledException)
                {
                    return new ProgramRunResult(
                        session.IsStopRequested ? ProgramRunOutcome.Stopped : ProgramRunOutcome.Failed,
                        session.IsStopRequested ? "Stopped." : $"Alarm: {latest.Status}",
                        linesSent, linesOk);
                }

                if (IsAlarm(latest.Status))
                    return new ProgramRunResult(ProgramRunOutcome.Failed, $"Alarm: {latest.Status}", linesSent, linesOk);

                if (reply.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new ProgramLineUpdate(entry.Sequence, ProgramLineStatus.Error, reply));
                    return new ProgramRunResult(ProgramRunOutcome.Failed, reply, linesSent, linesOk);
                }

                progress?.Report(new ProgramLineUpdate(entry.Sequence, ProgramLineStatus.Ok, null));
                linesOk++;

                // The program end (M2/M30 or closing %): stop streaming; trailing
                // rows in the file never reach the controller.
                if (entry.IsProgramEnd)
                    break;
            }

            return new ProgramRunResult(ProgramRunOutcome.Completed, null, linesSent, linesOk);
        }
        catch (OperationCanceledException)
        {
            return new ProgramRunResult(
                session.IsStopRequested ? ProgramRunOutcome.Stopped : ProgramRunOutcome.Failed,
                session.IsStopRequested ? "Stopped." : "Operation cancelled.",
                linesSent, linesOk);
        }
        catch (Exception ex)
        {
            return new ProgramRunResult(ProgramRunOutcome.Failed, ex.Message, linesSent, linesOk);
        }
    }
}
