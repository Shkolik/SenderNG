
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
    // Stub for GrblViewModel - minimal for compilation
    public class GrblViewModel
    {
        public bool Silent { get; set; }
        public bool ResponseLogVerbose { get; set; }
        public System.Collections.ObjectModel.ObservableCollection<string> ResponseLog { get; } = new();
        public event System.Action<string>? OnResponseReceived;
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
}
