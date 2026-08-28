using System.Text.Json;

namespace CNC.Core.Services;

/// <summary>Observable per-widget offsets configuration (tool-table visibility).</summary>
public sealed class OffsetsConfig : CNC.Core.ViewModelBase
{
    private bool _showToolTable = true;

    /// <summary>Whether the tool table is shown in the Offsets editor. Hidden on
    /// controllers that can't persist one (classic grbl 1.1 has no G10 L1).</summary>
    public bool ShowToolTable
    {
        get => _showToolTable;
        set => SetField(ref _showToolTable, value);
    }
}

/// <summary>
/// Reads/writes the "Offsets" section of <c>AppSettings.Modules</c>:
/// <c>{ "ShowToolTable": bool }</c>. Global (not mode-aware) like the Goto config —
/// the tool table is a property of the controller. Handles both the in-memory
/// Dictionary and the freshly deserialized JsonElement shapes.
/// </summary>
public static class OffsetsConfigStore
{
    private const string SectionKey = "Offsets";

    public static OffsetsConfig Load(AppSettings settings)
    {
        var config = new OffsetsConfig();
        if (settings.Modules.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } section &&
            ReadBool(section, "ShowToolTable") is { } show)
        {
            config.ShowToolTable = show;
        }
        return config;
    }

    public static void Save(AppSettings settings, OffsetsConfig config)
    {
        Dictionary<string, object?> section;
        if (settings.Modules.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];
        section["ShowToolTable"] = config.ShowToolTable;
        settings.Modules[SectionKey] = section;
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

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> entry, string key) =>
        entry.TryGetValue(key, out var v) && v is bool b ? b : null;
}