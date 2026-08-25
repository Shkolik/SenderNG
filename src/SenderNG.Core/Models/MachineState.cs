using CNC.Core;

namespace CNC.Core.Services;

public class MachineState
{
    public int NumAxes { get; set; } = 3;
    public string Status { get; set; } = "Idle";
    public double[] Positions { get; set; } = new double[3];
    public Signals Signals { get; set; } = Signals.Off;
    public bool Flood { get; set; }
    public bool Mist { get; set; }

    // Live values (grbl 1.1 / grblHAL status report FS: field).
    public double Feed { get; set; }
    public double Speed { get; set; }

    // Override percentages (Ov: field): feed, rapid, spindle/speed.
    public int FeedOvr { get; set; } = 100;
    public int RapidOvr { get; set; } = 100;
    public int SpindleOvr { get; set; } = 100;

    public MachineState() { }

    public MachineState(int numAxes)
    {
        NumAxes = numAxes;
        Positions = new double[numAxes];
    }
}
