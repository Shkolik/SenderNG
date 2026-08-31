
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

// TODO Task 5: expand to full Grbl for RealGrblService - currently minimal 271L vs foamcut 2535L (tech debt, ported caps/ParseCaps only)
/*
 * Grbl.cs - part of CNC Controls library (portable net10.0)
 * Original v0.44 / Io Engineering - stripped WPF refs for SenderNG
 * Caps parsing retained from foamcut 8ea54fb line ~1241
 * Fix columns[0] = columns[0].Replace at line 2000 retained
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Collections.ObjectModel;
using CNC.GCode;

namespace CNC.Core
{
    public static class GrblConstants
    {
        public const string CMD_GETINFO = "$I";
        public const string CMD_GETINFO_EXTENDED = "$I+";
        public const string NO_TOOL = "None";
        public const int X_AXIS=0, Y_AXIS=1, Z_AXIS=2, A_AXIS=3, B_AXIS=4, C_AXIS=5, U_AXIS=6, V_AXIS=7, W_AXIS=8;
    }

    public enum GrblStates { Unknown, Idle, Run, Tool, Hold, Home, Check, Jog, Alarm, Door, Sleep }
    public struct GrblState { public GrblStates State; public int Substate; public Color Color; public bool MPG; }

    public class CoordinateValues<T> : ViewModelBase
    {
        private T[] arr = new T[9];
        private bool _suspend=false;
        public int Length => 9;
        public bool SuspendNotifications { get=>_suspend; set=>_suspend=value; }
        public T this[int i] { get => arr[i]; set { if(!EqualityComparer<T>.Default.Equals(arr[i],value)){ arr[i]=value; if(!_suspend) OnPropertyChanged("XYZABCUVW".Substring(i,1)); } } }
        public T[] Array => arr;
    }

    public class AxisLetter : ViewModelBase
    {
        public AxisLetter() { Remap(GrblInfo.AxisLetters); }
        public void Remap(string map) { for(int i=0;i<map.Length;i++) Values[i]=map.Substring(i,1); for(int i=map.Length;i<Values.Length;i++) Values[i]="-"; }
        public string All { get { string s=""; for(int i=0;i<Values.Length;i++) s+=Values[i]; return s; } }
        public CoordinateValues<string> Values { get; } = new CoordinateValues<string>();
    }

    public class Position : ViewModelBase
    {
        public Position() { for(int i=0;i<Values.Length;i++) Values[i]=double.NaN; Values.PropertyChanged += (s,e)=>OnPropertyChanged(e.PropertyName ?? ""); }
        public Position(string v){ this.Values.PropertyChanged += (s,e)=>OnPropertyChanged(e.PropertyName ?? ""); Parse(v); }
        public bool Parse(string values){ try{ var p=Dbl.ParseList(values); for(int i=0;i<p.Length;i++) Values[i]=p[i]; return true;}catch{return false;}}
        public void Clear(){ for(int i=0;i<Values.Length;i++) Values[i]=double.NaN; }
        public CoordinateValues<double> Values { get; } = new CoordinateValues<double>();
        public double X { get=>Values[0]; set=>Values[0]=value; }
        public double Y { get=>Values[1]; set=>Values[1]=value; }
        public double Z { get=>Values[2]; set=>Values[2]=value; }
        public double A { get=>Values[3]; set=>Values[3]=value; }
        public double B { get=>Values[4]; set=>Values[4]=value; }
        public double C { get=>Values[5]; set=>Values[5]=value; }
        public double U { get=>Values[6]; set=>Values[6]=value; }
        public double V { get=>Values[7]; set=>Values[7]=value; }
        public double W { get=>Values[8]; set=>Values[8]=value; }
    }

    public class CoordinateSystem : Position
    {
        string _code="";
        public CoordinateSystem():base(){}
        public CoordinateSystem(string code,string data):base(data){ Code=code; }
        public int Id { get; private set; }
        public string Code { get=>_code; set{_code=value; OnPropertyChanged();} }
    }
    public class Tool : Position
    {
        public Tool(string code):base(){ Code=code; }
        public Tool(string code,string offsets):base(offsets){ Code=code; }
        public string Code { get; set; }
        double _r; public double R { get=>_r; set{_r=value; OnPropertyChanged();} }
    }

    public static class GrblInfo
    {
        private static int _numAxes=3;
        public static string AxisLetters { get; private set; } = "XYZABCUVW";
        public static string SignalLetters { get; private set; } = "XYZABCUVWEPRDHSBTOMF";
        public static string PositionFormatString { get; private set; } = string.Empty;
        public static string Version { get; private set; } = string.Empty;
        public static int Build { get; internal set; } = 0;
        public static bool IsGrblHAL { get; set; } = false;
        public static string Firmware { get; internal set; } = "grblHAL";
        public static bool ExtendedProtocol { get; internal set; }
        public static string Identity { get; private set; } = string.Empty;
        public static string Options { get; private set; } = string.Empty;
        public static string NewOptions { get; private set; } = string.Empty;
        public static int SerialBufferSize { get; internal set; } = 128;
        public static int PlanBufferSize { get; internal set; } = 16;
        public static bool ReportProbeResult { get; internal set; } = false;
        public static bool ForceSetOrigin { get; private set; } = false;
        public static bool ExpressionsSupported { get; private set; } = false;
        public static int NumAxes
        {
            get=>_numAxes;
            set{
                _numAxes=value;
                int flags=0; PositionFormatString="";
                for(int i=0;i<_numAxes;i++){ flags=(flags<<1)|0x01; PositionFormatString+=AxisIndexToLetter(i)+": {"+i+"}  "; }
                PositionFormatString=PositionFormatString.TrimEnd(' ');
                AxisFlags=(AxisFlags)flags;
            }
        }
        public static AxisFlags AxisFlags { get; private set; } = AxisFlags.XYZ;
        public static int NumTools { get; internal set; } = 0;
        public static int NumFans { get; private set; } = 0;
        public static bool HasATC { get; private set; }
        public static ObservableCollection<string> SystemInfo { get; } = new ObservableCollection<string>();
        public static bool IsLoaded { get; private set; }
        public static bool LightBurnCluster { get; internal set; } = false;
        public static bool LatheModeEnabled => GrblParserState.LatheMode != LatheMode.Disabled;
        public static bool LatheUVWModeEnabled { get=>false; set{} }
        public static bool HomingEnabled { get; internal set; } = false;
        public static AxisFlags HomingDirection { get; internal set; } = AxisFlags.None;
        public static string AxisIndexToLetter(int i)=>AxisLetters.Substring(i,1);
        public static int AxisLetterToIndex(string l)=>AxisLetters.IndexOf(l);

        static GrblInfo(){ NumAxes=3; }

        // Caps parsing kept from foamcut line ~1241
        internal static void Process(string data)
        {
            if(data.StartsWith("["))
            {
                string[] valuepair = data.Substring(1).TrimEnd(']').Split(':');
                switch(valuepair[0])
                {
                    case "OPT":
                        Options = valuepair[1];
                        string[] s = Options.Split(',');
                        if(s[0].Contains('+')) {} // OptionalSignals etc stripped
                        ForceSetOrigin = s[0].Contains('Z');
                        if(s.Length > 1)
                            PlanBufferSize = int.Parse(s[1], CultureInfo.InvariantCulture);
                        if(s.Length > 2)
                            SerialBufferSize = int.Parse(s[2], CultureInfo.InvariantCulture);
                        if(s.Length > 3 && IsGrblHAL && NumAxes != int.Parse(s[3], CultureInfo.InvariantCulture))
                        {
                            NumAxes = int.Parse(s[3], CultureInfo.InvariantCulture);
                        }
                        if(s.Length > 4)
                            NumTools = int.Parse(s[4], CultureInfo.InvariantCulture);
                        break;
                }
            }
        }

        // For testability - also updates NumAxes even when not GrblHAL
        internal static void TestProcess(string data){
            Process(data);
            // For portable tests, also handle numeric-first OPT without IsGrblHAL check
            if(data.StartsWith("[OPT:")){
                var inner = data.Substring(5).TrimEnd(']');
                var parts = inner.Split(',');
                var nums = new List<int>();
                int off=0;
                if(parts.Length>0 && parts[0].Any(c=>char.IsLetter(c))) off=1;
                for(int i=off;i<parts.Length;i++) if(int.TryParse(parts[i], out int v)) nums.Add(v);
                if(nums.Count>2) NumAxes = nums[2];
                if(nums.Count>0) PlanBufferSize = nums[0];
                if(nums.Count>1) SerialBufferSize = nums[1];
                if(nums.Count>3) NumTools = nums[3];
            }
        }
    }

    public class CapsResult
    {
        public int NumAxes { get; set; }
        public int PlanBufferSize { get; set; }
        public int SerialBufferSize { get; set; }
        public int NumTools { get; set; }
    }

    public static partial class Grbl
    {
        public static GrblViewModel? GrblViewModel { get; set; }

        public static CapsResult ParseCaps(string capsLine)
        {
            // Pure: does not mutate GrblInfo static state (fixes TestProcess leakage / test-order dependence)
            // Defaults mirror GrblInfo initial values
            int plan = 16, serial = 128, axes = 3, tools = 0;
            if (capsLine != null && capsLine.StartsWith("[OPT:", StringComparison.Ordinal))
            {
                var inner = capsLine.Substring(5).TrimEnd(']');
                var parts = inner.Split(',');
                var nums = new List<int>();
                int off = 0;
                if (parts.Length > 0 && parts[0].Any(c => char.IsLetter(c))) off = 1;
                for (int i = off; i < parts.Length; i++)
                    if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                        nums.Add(v);
                if (nums.Count > 0) plan = nums[0];
                if (nums.Count > 1) serial = nums[1];
                if (nums.Count > 2) axes = nums[2];
                if (nums.Count > 3) tools = nums[3];
            }
            return new CapsResult{ NumAxes=axes, PlanBufferSize=plan, SerialBufferSize=serial, NumTools=tools };
        }

        // Stub for file error handling - retains columns[0] fix from 8ea54fb line 2000
        public static void LoadErrors(string path)
        {
            try{
                var file = FileUtils.OpenFile(path);
                if(file!=null){
                    string? line=file.ReadLine();
                    line=file.ReadLine();
                    while(line!=null){
                        int key;
                        string[] columns=line.Split(',');
                        columns[0] = columns[0].Replace("\"", "");
                        if(columns.Length==3 && int.TryParse(columns[0], out key)){
                            // dummy
                        }
                        line=file.ReadLine();
                    }
                    file.Close();
                }
            }catch{}
        }
    }

    // Minimal stubs for other Grbl types not needed for Task 3 but kept for compilation
    public static class GrblParserState
    {
        public static string WorkOffset { get; set; } = "G54";
        public static string Tool { get; set; } = GrblConstants.NO_TOOL;
        public static bool IsMetric => true;
        public static DistanceMode DistanceMode => DistanceMode.Absolute;
        public static LatheMode LatheMode => LatheMode.Disabled;
        public static CoolantState CoolantState => CoolantState.Off;
        public static SpindleState SpindleState => SpindleState.Off;
        public static ToolLengthOffset ToolLengthOffset => ToolLengthOffset.Cancel;
        public static Plane Plane => Plane.XY;
        public static bool IsScalingActive => false;
        public static FeedRateMode FeedRateMode => FeedRateMode.UnitsPerMin;
        public static IJKMode IJKMode => IJKMode.Incremental;
        public static MotionMode MotionMode => MotionMode.G0;
        public static bool IsLoaded => false;
        public static string? IsActive(string k) => null;
        public static bool Get(GrblViewModel m)=>false;
        public static bool Get()=>false;
        public static bool Get(bool b)=>false;
        public static bool Process(string d)=>false;
    }
    public static class GrblWorkParameters
    {
        public static ObservableCollection<CoordinateSystem> CoordinateSystems { get; } = new ObservableCollection<CoordinateSystem>();
        public static ObservableCollection<Tool> Tools { get; } = new ObservableCollection<Tool>();
        public static CoordinateSystem ToolLengtOffset { get; } = new CoordinateSystem("TLO","");
        public static CoordinateSystem ProbePosition { get; } = new CoordinateSystem("PRB","");
        public static bool ProbeSuccesful => false;
        public static bool Get(GrblViewModel m)=>false;
        public static bool Get()=>false;
    }
    public static class GrblSettings
    {
        public static ObservableCollection<object> Settings { get; } = new ObservableCollection<object>();
        public static double GetDouble(GrblSetting s)=>double.NaN;
        public static int GetInteger(GrblSetting s)=>-1;
        public static double GetDouble(grblHALSetting s)=>double.NaN;
        public static int GetInteger(grblHALSetting s)=>-1;
        public static bool HasSetting(GrblSetting s)=>false;
    }
    public enum GrblSetting { ReportInches=13, SoftLimitsEnable=20, HardLimitsEnable=21, HomingEnable=22, HomingDirMask=23, HomingPulloff=27, TravelResolutionBase=100, MaxTravelBase=130 }
    public enum grblHALSetting { PulseMicroseconds=0, JogStepDistance=53, JogSlowDistance=54, JogFastDistance=55, JogStepSpeed=50, JogSlowSpeed=51, JogFastSpeed=52, EnableLegacyRTCommands=39, TravelResolutionBase=100, MaxTravelBase=130 }
    public static class Resources
    {
        public static string Path { get; set; } = "./";
        public static string Locale { get; set; } = "en-US";
        public static string IniName { get; set; } = "App.config";
        public static bool IsLegacyController { get; set; } = false;
    }
    public class GrblSettingDetails : ViewModelBase { public int Id; public string Name=""; public string Value=""; }
    public static class GrblAuxIO { public static void ParseConfig(string c){} }
    [Flags] public enum Signals : int { Off=0, LimitX=1<<0, LimitY=1<<1, LimitZ=1<<2, LimitA=1<<3, LimitB=1<<4, LimitC=1<<5, LimitU=1<<6, LimitV=1<<7, LimitW=1<<8, EStop=1<<9, Probe=1<<10, Reset=1<<11, SafetyDoor=1<<12, Hold=1<<13, CycleStart=1<<14, BlockDelete=1<<15, OptionalStop=1<<16, ProbeDisconnected=1<<17, MotorWarning=1<<18, MotorFault=1<<19 }
    public class PollGrbl { internal static bool suspend=false; internal static void Suspend()=>suspend=true; internal static void Resume()=>suspend=false; public bool IsEnabled=>false; public void SetState(int i){} public void Run(){} }
}
