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
