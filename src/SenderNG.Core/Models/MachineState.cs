
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

using CNC.Core;

namespace CNC.Core.Services;

/// <summary>Spindle direction as reported by the grblHAL accessory ('A:') field.</summary>
public enum SpindleDirection
{
    Off,
    Forward, // 'S'
    Reverse, // 'C'
}

public class MachineState
{
    public int NumAxes { get; set; } = 3;
    public string Status { get; set; } = "Idle";
    public double[] Positions { get; set; } = new double[3];

    // Machine coordinates (MPos:) — independent of work offsets. Null until the
    // controller reports them (grblHAL status reports carry both WPos and MPos).
    public double[]? MachinePositions { get; set; }

    // Work offsets (WCO:) from the status report. Classic grbl (default $10=1)
    // reports MPos + WCO and no WPos; the parser derives WPos = MPos - WCO.
    public double[]? WorkOffsets { get; set; }

    public Signals Signals { get; set; } = Signals.Off;

    // Signals the controller reports as available (discovered from $I and dynamic
    // Pn: observation) — drives which optional LEDs the signals panel shows.
    // Sticky: unlike Signals, it accumulates (ioSender OptionalSignals semantics).
    public Signals OptionalSignals { get; set; } = Signals.Off;

    public bool Flood { get; set; }
    public bool Mist { get; set; }

    // Accessory state from the grblHAL 'A:' field (classic grbl has no field).
    public bool IsToolChanging { get; set; }
    public SpindleDirection SpindleState { get; set; } = SpindleDirection.Off;

    // Live values (grbl 1.1 / grblHAL status report FS: field).
    public double Feed { get; set; }
    public double Speed { get; set; }

    // Override percentages (Ov: field): feed, rapid, spindle/speed.
    public int FeedOvr { get; set; } = 100;
    public int RapidOvr { get; set; } = 100;
    public int SpindleOvr { get; set; } = 100;

    // Probe touch position from a [PRB:...] report; null until a probe completes.
    public double[]? ProbePosition { get; set; }
    public bool ProbeSuccess { get; set; }

    // Homing state. Classic grbl's status report carries no homed flag, so this
    // is derived: true after a completed $H (demo + real services), false on any
    // Alarm report (an alarm always requires homing) or a soft reset.
    public bool IsHomed { get; set; }

    // Executing program line from the status report's Ln: field (grblHAL and
    // newer grbl report it while a program is running). 0 = no program line
    // reported. Note: it counts N-word lines when the file has them, else the
    // controller's internal block counter — matching it to file lines reliably
    // needs the (deferred) N-numbering decision, so the UI keys the executing
    // highlight off the sender instead and treats Ln: as informational for now.
    public int LineNumber { get; set; }

    public MachineState()
    {
        MachinePositions = new double[Positions.Length];
        WorkOffsets = new double[Positions.Length];
    }

    public MachineState(int numAxes)
    {
        NumAxes = numAxes;
        Positions = new double[numAxes];
        MachinePositions = new double[numAxes];
        WorkOffsets = new double[numAxes];
    }
}
