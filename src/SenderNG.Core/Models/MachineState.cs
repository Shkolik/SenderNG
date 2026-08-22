using CNC.Core;

namespace CNC.Core.Services;

public class MachineState
{
    public int NumAxes { get; set; } = 3;
    public string Status { get; set; } = "Idle";
    public double[] Positions { get; set; } = new double[3];
    public Signals Signals { get; set; } = Signals.Off;

    public MachineState() { }

    public MachineState(int numAxes)
    {
        NumAxes = numAxes;
        Positions = new double[numAxes];
    }
}
