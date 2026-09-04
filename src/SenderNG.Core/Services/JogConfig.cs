
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

using System.Text.Json;

namespace CNC.Core.Services;

/// <summary>Observable per-mode jog configuration: bindable directly from settings UIs.</summary>
public class JogConfig : CNC.Core.ViewModelBase
{
    private double[] _distances = [0.1, 1.0, 10.0];
    private double[] _speeds = [500.0, 1000.0, 2000.0];
    private int _distanceIndex = 1;
    private int _speedIndex = 1;
    private bool _diagonals = true;

    public double[] Distances { get => _distances; set => SetField(ref _distances, value); }
    public double[] Speeds { get => _speeds; set => SetField(ref _speeds, value); }

    public int DistanceIndex { get => _distanceIndex; set => SetField(ref _distanceIndex, value); }
    public int SpeedIndex { get => _speedIndex; set => SetField(ref _speedIndex, value); }

    public bool Diagonals { get => _diagonals; set => SetField(ref _diagonals, value); }

    // Named per-step accessors for editors; write through to the serialized arrays.
    public double Distance1 { get => Distances[0]; set { Distances[0] = value; OnPropertyChanged(); } }
    public double Distance2 { get => Distances[1]; set { Distances[1] = value; OnPropertyChanged(); } }
    public double Distance3 { get => Distances[2]; set { Distances[2] = value; OnPropertyChanged(); } }
    public double Speed1 { get => Speeds[0]; set { Speeds[0] = value; OnPropertyChanged(); } }
    public double Speed2 { get => Speeds[1]; set { Speeds[1] = value; OnPropertyChanged(); } }
    public double Speed3 { get => Speeds[2]; set { Speeds[2] = value; OnPropertyChanged(); } }
}
// Reads/writes the "Jog" section of a MachineProfile's ModuleSettings:
// { Distances, Speeds, DistanceIndex, SpeedIndex, Diagonals }.
// Per profile since the settings split — no mode nesting anymore (the legacy
// { Mill: {...}, Foam: {...} } shape was unwrapped by SettingsMigrator). The
// section may be a Dictionary<string,object> (in-memory) or a JsonElement
// (freshly deserialized profile file), so both shapes are handled defensively.
public static class JogConfigStore
{
    private const string SectionKey = "Jog";
    private const int Size = 3;

    /// <summary>Number of distance/speed steps per selector.</summary>
    public static int StepCount => Size;

    public static JogConfig Load(MachineProfile profile)
    {
        var config = new JogConfig();

        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } entry)
        {
            if (ReadArray(entry, "Distances") is { Count: > 0 } distances)
                config.Distances = [.. distances];
            if (ReadArray(entry, "Speeds") is { Count: > 0 } speeds)
                config.Speeds = [.. speeds];
            if (ReadDouble(entry, "DistanceIndex") is { } di)
                config.DistanceIndex = (int)di;
            if (ReadDouble(entry, "SpeedIndex") is { } si)
                config.SpeedIndex = (int)si;
            if (ReadBool(entry, "Diagonals") is { } diag)
                config.Diagonals = diag;
        }

        return Normalize(config);
    }

    public static void Save(MachineProfile profile, JogConfig config)
    {
        Dictionary<string, object?> section;
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];

        // Normalize before writing so the profile always persists a valid shape.
        var normalized = Normalize(config);
        section["Distances"] = normalized.Distances.ToList();
        section["Speeds"] = normalized.Speeds.ToList();
        section["DistanceIndex"] = normalized.DistanceIndex;
        section["SpeedIndex"] = normalized.SpeedIndex;
        section["Diagonals"] = normalized.Diagonals;
        profile.ModuleSettings[SectionKey] = section;
    }

    private static JogConfig Normalize(JogConfig config)
    {
        config.Distances = Clamp(config.Distances);
        config.Speeds = Clamp(config.Speeds);
        config.DistanceIndex = Math.Clamp(config.DistanceIndex, 0, Size - 1);
        config.SpeedIndex = Math.Clamp(config.SpeedIndex, 0, Size - 1);
        return config;
    }

    private static double[] Clamp(double[]? values)
    {
        values ??= [];
        var result = new List<double>(values.Take(Size));
        while (result.Count < Size)
            result.Add(result.Count > 0 ? result[^1] : 0.0);
        return [.. result];
    }

    private static Dictionary<string, object?>? AsObject(object? value) => value switch
    {
        Dictionary<string, object> typed => typed.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
        JsonElement { ValueKind: JsonValueKind.Object } el => ReadObject(el),
        _ => null,
    };

    private static Dictionary<string, object?> ReadObject(JsonElement el)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in el.EnumerateObject())
            result[prop.Name] = prop.Value;
        return result;
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, object?> entry, string key) =>
        entry.TryGetValue(key, out var v) ? ToDouble(v) : null;

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> entry, string key) =>
        entry.TryGetValue(key, out var v) && v is bool b ? b : null;

    private static List<double>? ReadArray(IReadOnlyDictionary<string, object?> entry, string key)
    {
        if (!entry.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            IEnumerable<double> doubles => doubles.ToList(),
            IEnumerable<int> ints => ints.Select(i => (double)i).ToList(),
            JsonElement { ValueKind: JsonValueKind.Array } arr =>
                arr.EnumerateArray().Select(e => e.TryGetDouble(out var d) ? d : 0.0).ToList(),
            _ => null,
        };
    }

    private static double? ToDouble(object? value) => value switch
    {
        double d => d,
        int i => i,
        long l => l,
        JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetDouble(out var d) => d,
        _ => null,
    };
}
