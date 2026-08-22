// Portable replacements for WPF types
namespace CNC.Core
{
    public struct Point
    {
        public double X { get; set; }
        public double Y { get; set; }
        public Point(double x, double y) { X = x; Y = y; }
    }

    public struct Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
        public override string ToString() => $"{X},{Y},{Z}";
    }

    // Minimal stubs for WPF dependencies not needed in portable core
    public static class EventUtils
    {
        public static void DoEvents() { System.Threading.Thread.Sleep(1); }
    }

    // Stub for Comms - not used in portable tests but required for Grbl.cs compilation
    public class Comms
    {
        public static Comms com = new Comms();
        public string Reply { get; set; } = string.Empty;
        public void WriteByte(byte b) { }
        public void WriteCommand(string cmd) { }
        public void PurgeQueue() { }
        public void AwaitAck() { }
    }

    // Stub for GrblViewModel - minimal for compilation
    public class GrblViewModel
    {
        public bool Silent { get; set; }
        public bool ResponseLogVerbose { get; set; }
        public System.Collections.ObjectModel.ObservableCollection<string> ResponseLog { get; } = new();
        public event System.Action<string>? OnResponseReceived;
        public event System.Action<string>? OnWCOUpdated;
        public PollGrbl Poller { get; } = new PollGrbl();
        public string Firmware { get; set; } = "grblHAL";
        public int NumAxes { get; set; }
        public CNC.GCode.AxisFlags AxisEnabledFlags { get; set; }
        public bool LatheModeEnabled { get; set; }
        public SignalsWrapper OptionalSignals { get; } = new SignalsWrapper();
        public GrblState GrblState { get; set; }
        public bool IsParserStateLive { get; set; }
        public bool IsMetric { get; set; }
        public KeypressHandlerStub Keyboard { get; } = new KeypressHandlerStub();
        public void ClearPosition() { }
        public void ClearSignals() { }
        public void ParseStatus(string s) { }
        public GrblState GrblStateValue { get; set; }
        public string Tool { get; set; } = "";
        public Position ToolOffset { get; } = new Position();
        public AxisLetter AxisLetter { get; } = new AxisLetter();
        public void ExecuteCommand(string cmd) => OnResponseReceived?.Invoke("ok");
    }

    public class SignalsWrapper
    {
        public Signals Value { get; set; }
    }

    public class KeypressHandlerStub
    {
        public bool IsContinuousJoggingEnabled { get; set; }
        public bool SoftLimits { get; set; }
        public double LimitSwitchesClearance { get; set; }
        public double JogStepDistance { get; set; }
        public double[] JogDistances { get; } = new double[3];
        public double[] JogFeedrates { get; } = new double[3];
    }

    public class DispatcherStub
    {
        public void Invoke(System.Action<string> action, string data) => action(data);
        public static DispatcherStub CurrentDispatcher => new DispatcherStub();
    }
}
