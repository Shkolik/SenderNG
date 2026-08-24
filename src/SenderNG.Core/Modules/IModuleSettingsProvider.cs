namespace SenderNG.Core.Modules;

/// <summary>Modules implement this to expose editable fields in App Settings.</summary>
public interface IModuleSettingsProvider
{
    /// <summary>Numeric fields; default none (prefer CreateSettingsView).</summary>
    IEnumerable<ModuleSettingField> GetSettingFields() => [];

    /// <summary>Optional boolean fields (checkboxes); default none.</summary>
    IEnumerable<ModuleSettingBool> GetBoolSettingFields() => [];

    /// <summary>Module-provided settings UI, injected into the App Settings card.
    /// When non-null it replaces the generic field lists.</summary>
    object? CreateSettingsView() => null;

    /// <summary>Called once after host build so the module can resolve services
    /// (settings fields may be read before the module's own UI is ever opened).</summary>
    void AttachServices(IServiceProvider services) { }
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
