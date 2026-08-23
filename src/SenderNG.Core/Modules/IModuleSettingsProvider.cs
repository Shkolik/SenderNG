namespace SenderNG.Core.Modules;

/// <summary>Modules implement this to expose editable numeric fields in App Settings.</summary>
public interface IModuleSettingsProvider
{
    IEnumerable<ModuleSettingField> GetSettingFields();
}

public sealed class ModuleSettingField(string label, Func<double> get, Action<double> set)
{
    public string Label { get; } = label;
    public Func<double> Get { get; } = get;
    public Action<double> Set { get; } = set;
}
