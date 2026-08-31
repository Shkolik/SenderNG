
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

// Helpers.cs - portable replacements for CNC.Core.HelperClasses
// Original: HelperClasses.cs v0.36 / Io Engineering
// Stripped WPF refs (System.Windows, PresentationCore, System.Management) for net10.0 portable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Collections;

namespace CNC.Core
{
    // Localisation strings (English defaults ported from ioSender LibStrings.xaml)
    public static class LibStrings
    {
        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["LoadError"] = "{0}\nLine: {1}\nBlock: {2}\n\nContinue loading?",
            ["ParserStrip"] = "{0} command found, strip?",
            ["ParserStripHdr"] = "Strip command",
            ["ParserBadExpr"] = "Bad expression",
            ["ParserUnsupportedCmd"] = "Unsupported command",
            ["ParserAxisError"] = "Axis command conflict",
            ["ParserModalGrpError"] = "Modal group violation",
            ["ParserCmdUnknown"] = "Command word not recognized",
            ["ParserCMDInvalid"] = "Invalid GCode",
            ["ParserWordRepeated"] = "Command word repeated",
            ["ParserToolProfile"] = "Tool {0} not associated with a profile",
            ["ParserM66PandE"] = "Cannot use both P- and E-word with M66",
            ["ParserM66NoPorE"] = "P- or E-word missing for M66",
            ["ParserM66BadParams"] = "Illegal M66 parameters",
            ["ParserG6NoP"] = "G4 - missing P word",
            ["ParserNoG0orG1"] = "G0 or G1 not active",
            ["ParserG1NoFeed"] = "G1 used when feed rate is not set",
            ["ParserPlaneNotXY"] = "Plane not XY",
            ["ParserNoPandorQ"] = "P and/or Q word missing",
            ["ParserNoIandorJ"] = "I and/or J word missing",
            ["ParserNoR"] = "R word missing",
            ["ParserPlaneNotZX"] = "Plane not ZX",
            ["ParserZPlus"] = "Axisword(s) other than Z found",
            ["ParserNoP"] = "P word missing",
            ["ParserNegP"] = "P word negative",
            ["ParserNoI"] = "I word missing",
            ["ParserNoJ"] = "J word missing",
            ["ParserNegJ"] = "J word negative",
            ["ParserNoK"] = "K word missing",
            ["ParserNegK"] = "K word negative",
            ["ParserKlesseqJ"] = "K word must be greater than J word",
            ["ParseRless1"] = "R word less than 1",
            ["ParserNegH"] = "H word is negative",
            ["ParserErrE"] = "E word greater than half the drive line length",
            ["ParserNoInvalidQ"] = "Q word missing or out of range",
            ["ParserRadiusErr"] = "Error computing arc radius.",
            ["SerialPortError"] = "Unable to open serial port: {0}",
            ["JoggingOnly"] = "Only jogging and some system commands are allowed when changing tool!",
            ["SdStreamComplete"] = "SD Card streaming {0}% complete",
            ["ContUnlock"] = "<Unlock> to continue",
            ["ContHomeUnlock"] = "<Home> or <Unlock> to continue",
            ["ContClearResetUnlock"] = "clear then <Reset> then <Unlock> to continue",
            ["ContHome"] = "<Home> to continue",
            ["ContResetUnlock"] = "<Reset> then <Unlock> to continue",
            ["ProbePrimary"] = "Primary",
            ["ProbeToolSetter"] = "Toolsetter",
            ["ProbeSecondary"] = "Secondary",
        };

        public static string FindResource(string key) =>
            Strings.TryGetValue(key, out var s) ? s : key;
    }

    // Portable ViewModelBase - no WPF dependencies
    public class ViewModelBase : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, ICollection<string>> _validationErrors = new Dictionary<string, ICollection<string>>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #region INotifyDataErrorInfo members
        public void ClearErrors()
        {
            var props = new List<string>(_validationErrors.Keys);
            _validationErrors.Clear();
            foreach (var p in props) RaiseErrorsChanged(p);
        }
        public void SetError(string message) => _validationErrors[string.Empty] = new List<string> { message };
        public void SetError(string property, string message)
        {
            if (_validationErrors.TryGetValue(property, out var v)) v.Add(message);
            else _validationErrors[property] = new List<string> { message };
            RaiseErrorsChanged(property);
        }
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
        private void RaiseErrorsChanged(string n) => ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(n));
        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) || !_validationErrors.ContainsKey(propertyName!)) return Array.Empty<string>();
            return _validationErrors[propertyName!];
        }
        public bool HasErrors => _validationErrors.Count > 0;
        #endregion
    }

    public static class Dbl
    {
        public static string ToInvariantString(this double value) => value.ToString(CultureInfo.InvariantCulture);
        public static string ToInvariantString(this double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        public static bool Assign(double value, ref double holder)
        {
            bool changed = double.IsNaN(value) ? !double.IsNaN(holder) : holder != value;
            if (changed) holder = value;
            return changed;
        }
        public static double[] ParseList(string s)
        {
            string[] v = s.Split(',');
            double[] values = new double[v.Length];
            for (int i = 0; i < v.Length; i++)
                if (!double.TryParse(v[i], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[i]))
                    values[i] = 0.0d;
            return values;
        }
        public static double Parse(string value)
        {
            double result = double.NaN;
            if (value != null)
            {
                value = value.Trim();
                if (value.Length == 0 || !double.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                    result = double.NaN;
            }
            return result;
        }
    }

    public static class FileUtils
    {
        public static StreamReader? OpenFile(string filename)
        {
            try { return new StreamReader(filename); } catch { return null; }
        }
    }

    // Portable color stub (replaces System.Windows.Media.Color)
    public struct Color
    {
        public byte R, G, B, A;
        public static Color FromRgb(byte r, byte g, byte b) => new Color { R = r, G = g, B = b, A = 255 };
    }

    // Minimal WaitFor stub - not used in portable core but kept for source compatibility
    public static class WaitFor
    {
        public static bool SingleEvent<T>(this System.Threading.CancellationToken token, Action<T> handler, Action<Action<T>> subscribe, Action<Action<T>> unsubscribe, int ms, Action? init = null) => false;
        public static bool AckResponse<T>(this System.Threading.CancellationToken token, Action<T> handler, Action<Action<T>> subscribe, Action<Action<T>> unsubscribe, int ms, Action? init = null) => false;
    }

    // Portable serial port enumeration stub (replaces System.Management)
    public static class SerialPortHelper
    {
        public static string[] GetPortNames()
        {
            try { return System.IO.Ports.SerialPort.GetPortNames(); } catch { return Array.Empty<string>(); }
        }
    }
}
