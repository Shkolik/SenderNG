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
    // Stub for localisation - returns empty or key
    public static class LibStrings
    {
        public static string FindResource(string key) => string.Empty;
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

    public static class dbl
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
