namespace SenderNG.Core.Modules;

/// <summary>Modules implement this to expose editable fields in App Settings.</summary>
public interface IModuleSettingsProvider
{
    IEnumerable<ModuleSettingField> GetSettingFields();

    /// <summary>Optional boolean fields (checkboxes); default none.</summary>
    IEnumerable<ModuleSettingBool> GetBoolSettingFields() => [];
}

public sealed class ModuleSettingField(string label, Func<double> get, Action<double> set)
{
    public string Label { get; } = label;
    public Func<double> Get { get; } = get;
    public Action<double> Set { get; } = set;
}

public sealed class ModuleSettingBool(string label, Func<bool> get, Action<bool> set)
{
    public string Label { get; } = label;
    public Func<bool> Get { get; } = get;
    public Action<bool> Set { get; } = set;
}
