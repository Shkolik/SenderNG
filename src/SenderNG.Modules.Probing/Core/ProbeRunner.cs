
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

using CNC.Core.Abstractions;
using CNC.Core.Services;

namespace SenderNG.Core.Modules.Probing;

/// <summary>Outcome of a probing run: success flag, captured probe touch positions and any message.</summary>
public sealed class ProbeRunResult
{
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
    public string? LastMessage { get; set; }

    /// <summary>Probe touch positions (machine coords), one per G38.x move, in program order.</summary>
    public List<double[]> Positions { get; } = new();
}

/// <summary>
/// Executes a probe program produced by <see cref="ProbeProgramBuilder"/>: each
/// line is sent to the controller and the machine is waited back to Idle before
/// the next move. Probe touch positions are captured from the controller state
/// (demo emulates them; real grbl reports them via [PRB:...]).
/// </summary>
public sealed class ProbeRunner
{
    private readonly IGrblService _grbl;

    public ProbeRunner(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
    }

    private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Runs the program lines. Lines starting with '#' are messages, '!' drops the
    /// previous probe position (ioSender's retract marker) and "pause" waits for
    /// cycle start (no-op in v0 - the pause is informational only).
    /// </summary>
    public async Task<ProbeRunResult> ExecuteAsync(IReadOnlyList<string> program, CancellationToken cancellationToken = default)
    {
        var result = new ProbeRunResult();

        foreach (var raw in program)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = raw;
            if (line.StartsWith('#'))
            {
                result.LastMessage = line[1..];
                continue;
            }

            if (line.StartsWith('!'))
            {
                line = line[1..];
                if (result.Positions.Count > 0)
                    result.Positions.RemoveAt(result.Positions.Count - 1);
            }

            if (line == "pause")
                continue;

            // Arm the idle gate BEFORE the move. Subscribing after the send would
            // replay the pre-move "Idle" (every service exposes a BehaviorSubject),
            // so the gate would be satisfied before the axis had even started and
            // the next line would go out while the machine was still travelling.
            var idleTask = WaitForIdleAsync(cancellationToken);

            await _grbl.SendAsync(line);

            if (!await idleTask)
                return new ProbeRunResult { Success = false, Error = "Machine did not return to Idle (alarm or timeout)" };

            // A G38.x move just completed: capture the probe touch position.
            if (line.Contains("G38", StringComparison.OrdinalIgnoreCase))
            {
                var latest = LatestState();
                if (latest?.ProbePosition is { } probePos)
                    result.Positions.Add((double[])probePos.Clone());
            }
        }

        return result;
    }

    private MachineState? LatestState()
    {
        // The services expose a BehaviorSubject, so the latest state is always replayed.
        MachineState? state = null;
        using var sub = _grbl.State.Subscribe(s => state = s);
        return state;
    }

    private async Task<bool> WaitForIdleAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The subscribe-time replay of the last state is dropped: it predates the
        // move we just sent, so an "Idle" in it proves nothing.
        var replay = true;
        using var sub = _grbl.State.Subscribe(s =>
        {
            if (replay)
            {
                replay = false;
                return;
            }
            if (s is null)
                return;
            var status = s.Status ?? string.Empty;
            if (status.Contains("alarm", StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(false);
            else if (status.Contains("idle", StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(true);
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(MoveTimeout);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
