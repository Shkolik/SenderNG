using System.Text.Json;

namespace CNC.Core.Services;

public class JogConfig
{
    public double[] Distances { get; set; } = [0.1, 1.0, 10.0];
    public double[] Speeds { get; set; } = [500.0, 1000.0, 2000.0];
    public int DistanceIndex { get; set; } = 1;
    public int SpeedIndex { get; set; } = 1;
    public bool Diagonals { get; set; } = true;
}

// Reads/writes the "Jog" section of AppSettings.Modules:
// { "Mill": { Distances, Speeds, DistanceIndex, SpeedIndex }, "Foam": {...} }.
// The section may be a Dictionary<string,object> (in-memory) or a JsonElement
// (freshly deserialized settings.json), so both shapes are handled defensively.
public static class JogConfigStore
{
    private const string SectionKey = "Jog";
    private const int Size = 3;

    /// <summary>Number of distance/speed steps per selector.</summary>
    public static int StepCount => Size;

    public static JogConfig Load(AppSettings settings)
    {
        var config = new JogConfig();

        if (settings.Modules.TryGetValue(SectionKey, out var raw))
        {
            var section = AsObject(raw);
            if (section is not null &&
                section.TryGetValue(ModeKey(settings), out var modeRaw) &&
                AsObject(modeRaw) is { } entry)
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
        }

        return Normalize(config);
    }

    public static void Save(AppSettings settings, JogConfig config)
    {
        Dictionary<string, object?> section;
        if (settings.Modules.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];

        // Normalize before writing so both modes always persist a valid shape.
        var normalized = Normalize(config);
        section[ModeKey(settings)] = new Dictionary<string, object?>
        {
            ["Distances"] = normalized.Distances.ToList(),
            ["Speeds"] = normalized.Speeds.ToList(),
            ["DistanceIndex"] = normalized.DistanceIndex,
            ["SpeedIndex"] = normalized.SpeedIndex,
            ["Diagonals"] = normalized.Diagonals,
        };
        settings.Modules[SectionKey] = section;
    }

    private static string ModeKey(AppSettings settings) =>
        settings.Mode == AppSettings.FoamMode ? AppSettings.FoamMode : AppSettings.MillMode;

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
        Dictionary<string, object> typed => new Dictionary<string, object?>(typed),
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
