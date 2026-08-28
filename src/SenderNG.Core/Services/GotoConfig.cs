using System.Text.Json;

namespace CNC.Core.Services;

/// <summary>Observable per-widget goto configuration (axis count preference).</summary>
public sealed class GotoConfig : CNC.Core.ViewModelBase
{
    private int _axisCount = 3;

    /// <summary>How many axes the user wants in the Goto widget; the controller's
    /// actual axis count caps the displayed rows (Min of the two).</summary>
    public int AxisCount
    {
        get => _axisCount;
        set => SetField(ref _axisCount, value);
    }
}

/// <summary>
/// Reads/writes the "Goto" section of <c>AppSettings.Modules</c>:
/// <c>{ "AxisCount": 4 }</c>. Global (not mode-aware) like the connection
/// settings — the goto axis preference is how the user drives the machine, and
/// the connected controller's reported axis count is the real cap regardless of
/// mode. The section may be a Dictionary (in-memory) or a JsonElement (freshly
/// deserialized settings.json), so both shapes are handled defensively.
/// </summary>
public static class GotoConfigStore
{
    private const string SectionKey = "Goto";

    public static GotoConfig Load(AppSettings settings)
    {
        var config = new GotoConfig();
        if (settings.Modules.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } section &&
            ReadDouble(section, "AxisCount") is { } axisCount)
        {
            config.AxisCount = (int)axisCount;
        }
        return Normalize(config);
    }

    public static void Save(AppSettings settings, GotoConfig config)
    {
        Dictionary<string, object?> section;
        if (settings.Modules.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];
        var normalized = Normalize(config);
        section["AxisCount"] = normalized.AxisCount;
        settings.Modules[SectionKey] = section;
    }

    private static GotoConfig Normalize(GotoConfig config)
    {
        config.AxisCount = Math.Clamp(config.AxisCount, 1, CNC.Core.GrblInfo.AxisLetters.Length);
        return config;
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
        entry.TryGetValue(key, out var v) ? v switch
        {
            double d => d,
            int i => i,
            long l => l,
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetDouble(out var d) => d,
            _ => null,
        } : null;
}
