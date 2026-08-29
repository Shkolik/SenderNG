using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace CNC.Core.ViewModels;

/* ---------------------------------------------------------------------------
 * Tree model
 * ------------------------------------------------------------------------- */

/// <summary>A group node in the App Settings tree. The TreeViewItem binds IsExpanded
/// two-way so expansion survives (mirrors GrblSettings' group node).</summary>
public sealed class AppSettingsGroupItem : ViewModelBase
{
    private bool _isExpanded;
    private string _name;

    public string Name
    {
        get => _name;
        internal set => SetField(ref _name, value);
    }
    public ObservableCollection<object> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public AppSettingsGroupItem(string name) => _name = name;

    internal void Relocalize(string name, string description)
    {
        _ = description;
        Name = name;
    }
}

/// <summary>Base for a selectable leaf in the App Settings tree.</summary>
public abstract class AppSettingsLeaf : ViewModelBase
{
    public string Id { get; }
    public string Name { get; protected set; }
    public string Description { get; protected set; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    protected AppSettingsLeaf(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    /// <summary>Re-name/re-summarize in the current locale (raises change notifications).</summary>
    internal void Relocalize(string name, string description)
    {
        var changed = !string.Equals(Name, name, StringComparison.Ordinal)
                    || !string.Equals(Description, description, StringComparison.Ordinal);
        Name = name;
        Description = description;
        if (changed)
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(HasDescription));
        }
    }
}

/// <summary>Global &gt; Mode leaf: Mill / Foam selector, persisted as the global mode.</summary>
public sealed class ModeSettingsLeaf : AppSettingsLeaf
{
    private readonly Action<string> _setMode;
    private string _mode;

    public ModeSettingsLeaf(string currentMode, Action<string> setMode, string name, string description)
        : base("mode", name, description)
    {
        _mode = currentMode;
        _setMode = setMode;
    }

    public bool IsMill
    {
        get => _mode == AppSettings.MillMode;
        set { if (value) SetMode(AppSettings.MillMode); }
    }

    public bool IsFoam
    {
        get => _mode == AppSettings.FoamMode;
        set { if (value) SetMode(AppSettings.FoamMode); }
    }

    private void SetMode(string mode)
    {
        if (_mode == mode)
            return;
        _mode = mode;
        _setMode(mode);
        OnPropertyChanged(nameof(IsMill));
        OnPropertyChanged(nameof(IsFoam));
    }
}

/// <summary>Global &gt; Language leaf: locale dropdown that persists and re-applies at runtime.</summary>
public sealed class LanguageSettingsLeaf : AppSettingsLeaf
{
    private string _selectedLocale;
    private readonly Action<string> _setLocale;

    public LanguageSettingsLeaf(string currentLocale, IReadOnlyList<string> availableLocales,
        Action<string> setLocale, string name, string description)
        : base("language", name, description)
    {
        _selectedLocale = string.IsNullOrWhiteSpace(currentLocale) ? "en" : currentLocale;
        AvailableLocales = availableLocales;
        _setLocale = setLocale;
        if (!AvailableLocales.Contains(_selectedLocale, StringComparer.OrdinalIgnoreCase))
            _selectedLocale = AvailableLocales.FirstOrDefault() ?? "en";
    }

    public IReadOnlyList<string> AvailableLocales { get; }

    public string SelectedLocale
    {
        get => _selectedLocale;
        set
        {
            if (string.Equals(_selectedLocale, value, StringComparison.OrdinalIgnoreCase))
                return;
            _selectedLocale = value;
            OnPropertyChanged();
            _setLocale(value);
        }
    }
}

/// <summary>One editable numeric field exposed by a module (e.g. Jog distances).</summary>
public sealed class NumericSettingEntry : ViewModelBase
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private double _value;

    public NumericSettingEntry(string label, Func<double> get, Action<double> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        Label = label;
        _get = get;
        _set = set;
        _value = get();
    }

    public string Label { get; }

    public double Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged(nameof(Value));
            _set(value);
        }
    }

    public void Refresh()
    {
        var v = _get();
        if (_value == v)
            return;
        _value = v;
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>One editable boolean field exposed by a module (e.g. Jog diagonals).</summary>
public sealed class BoolSettingEntry : ViewModelBase
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;
    private bool _value;

    public BoolSettingEntry(string label, Func<bool> get, Action<bool> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        Label = label;
        _get = get;
        _set = set;
        _value = get();
    }

    public string Label { get; }

    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged(nameof(Value));
            _set(value);
        }
    }

    public void Refresh()
    {
        var v = _get();
        if (_value == v)
            return;
        _value = v;
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>Modules &gt; module leaf: localizable name + optional description, a visibility toggle
/// (hideable modules only), and either a module-provided settings view or generic fields.</summary>
public sealed class ModuleSettingsLeaf : AppSettingsLeaf
{
    private readonly System.Action<string, bool> _visibilityChanged;
    private bool _isVisible;

    public ModuleSettingsLeaf(
        string id,
        string name,
        string description,
        bool isVisible,
        bool canHide,
        System.Action<string, bool> visibilityChanged,
        object? settingsView,
        IEnumerable<NumericSettingEntry>? fields,
        IEnumerable<BoolSettingEntry>? boolFields)
        : base(id, name, description)
    {
        CanHide = canHide;
        _isVisible = canHide ? isVisible : true; // fixed modules are always visible
        _visibilityChanged = visibilityChanged;
        SettingsView = settingsView;
        if (fields is not null)
            foreach (var f in fields)
                Fields.Add(f);
        if (boolFields is not null)
            foreach (var f in boolFields)
                BoolFields.Add(f);
    }

    public bool CanHide { get; }

    /// <summary>Fixed modules (DRO, Program, Console, Jog) render a plain-name header (no checkbox).</summary>
    public bool ShowNameOnly => !CanHide;

    /// <summary>Module-provided settings UI; when set it replaces the generic field lists.</summary>
    public object? SettingsView { get; }

    public bool ShowGenericFields => SettingsView is null;

    public bool ShowAlwaysVisibleCaption => ShowNameOnly && SettingsView is null;

    public ObservableCollection<NumericSettingEntry> Fields { get; } = new();
    public ObservableCollection<BoolSettingEntry> BoolFields { get; } = new();

    public void RefreshFields()
    {
        foreach (var f in Fields)
            f.Refresh();
        foreach (var f in BoolFields)
            f.Refresh();
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
                return;
            _isVisible = value;
            OnPropertyChanged(nameof(IsVisible));
            _visibilityChanged(Id, value);
        }
    }
}

/* ---------------------------------------------------------------------------
 * View model
 * ------------------------------------------------------------------------- */

public class AppSettingsViewModel : ViewModelBase, IClosableDialog
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localization;
    private readonly Dictionary<string, bool> _defaults = new();
    private readonly Dictionary<string, IModule> _moduleById;
    private readonly List<AppSettingsLeaf> _globalLeaves = new();
    private bool _updatingSections;
    private string _currentLanguage;

    public event EventHandler<bool>? CloseRequested;

    public AppSettingsViewModel(
        SettingsService settingsService,
        IEnumerable<IModule> modules,
        LocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(modules);

        _settingsService = settingsService;
        _localization = localization ?? DefaultLocalization();
        _settings = settingsService.Load();
        _currentLanguage = string.IsNullOrWhiteSpace(_settings.Language) ? "en" : _settings.Language;
        if (!_localization.AvailableLocales.Contains(_currentLanguage, StringComparer.OrdinalIgnoreCase))
            _currentLanguage = "en";

        _moduleById = modules.ToDictionary(m => m.Id);
        BuildModuleLeaves(modules);
        BuildTree();

        _localization.Changed += OnLocaleChanged;
        CloseCommand = new DelegateCommand(_ => RequestClose(true));
    }

    private static LocalizationService DefaultLocalization() =>
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private void BuildModuleLeaves(IEnumerable<IModule> modules)
    {
        foreach (var module in modules.OrderBy(m => m.Order))
        {
            _defaults[module.Id] = module.IsVisibleByDefault;
            IEnumerable<NumericSettingEntry>? fields = null;
            IEnumerable<BoolSettingEntry>? boolFields = null;
            object? settingsView = null;
            if (module is IModuleSettingsProvider provider)
            {
                settingsView = provider.CreateSettingsView();
                if (settingsView is null)
                {
                    fields = provider.GetSettingFields()
                        .Select(f => new NumericSettingEntry(f.Label, f.Get, f.Set))
                        .ToList();
                    boolFields = provider.GetBoolSettingFields()
                        .Select(f => new BoolSettingEntry(f.Label, f.Get, f.Set))
                        .ToList();
                }
            }

            // Fixed modules with nothing to configure have no leaf at all.
            if (!module.CanHide && settingsView is null && fields is null && boolFields is null)
                continue;

            ModuleLeaves.Add(new ModuleSettingsLeaf(
                module.Id,
                ModuleName(module),
                ModuleDescription(module),
                IsModuleVisible(module),
                module.CanHide,
                VisibilityChanged,
                settingsView,
                fields,
                boolFields));
        }
    }

    private void BuildTree()
    {
        Groups.Clear();

        var global = new AppSettingsGroupItem(GroupName("global", "Global"));
        _globalLeaves.Clear();
        var modeLeaf = new ModeSettingsLeaf(_settings.Mode, SetMode,
            LeafName("mode", "Mode"),
            LeafDescription("mode", "Machine mode: Mill for CNC milling, Foam for hot-wire foam cutting."));
        _globalLeaves.Add(modeLeaf);
        var languageLeaf = new LanguageSettingsLeaf(_currentLanguage, _localization.AvailableLocales, SetLocale,
            LeafName("language", "Language"),
            LeafDescription("language", "Interface language. Applied immediately."));
        _globalLeaves.Add(languageLeaf);
        foreach (var leaf in _globalLeaves)
            global.Children.Add(leaf);

        var modules = new AppSettingsGroupItem(GroupName("modules", "Modules"));
        foreach (var leaf in ModuleLeaves)
            modules.Children.Add(leaf);

        Groups.Add(global);
        Groups.Add(modules);

        global.IsExpanded = true;
        // Select the first leaf so the detail pane is populated on open.
        SelectedNode = modeLeaf;
    }

    private AppSettingsGroupItem GlobalGroup => (AppSettingsGroupItem)Groups[0];
    private AppSettingsGroupItem ModulesGroup => (AppSettingsGroupItem)Groups[1];

    private string GroupName(string key, string fallback) => Resolve($"appSettings.{key}", fallback);
    private string LeafName(string key, string fallback) => Resolve($"appSettings.leaf.{key}", fallback);
    private string LeafDescription(string key, string fallback) => Resolve($"appSettings.leaf.{key}.description", fallback);
    private string ModuleName(IModule module) => Resolve($"module.{module.Id}.name", module.Name);
    private string ModuleDescription(IModule module) => Resolve($"module.{module.Id}.description", module.Description);

    private string Resolve(string key, string fallback)
    {
        var v = _localization.T(key);
        return string.Equals(v, key, StringComparison.Ordinal) ? fallback : v;
    }

    /// <summary>Module leaves, ordered by module order.</summary>
    public ObservableCollection<ModuleSettingsLeaf> ModuleLeaves { get; } = new();

    /// <summary>The two top-level roots in the tree (Global, Modules).</summary>
    public ObservableCollection<object> Groups { get; } = new();

    public DelegateCommand CloseCommand { get; }

    /// <summary>TreeView SelectedItem — only leaves select; a group click is discarded.</summary>
    public object? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetField(ref _selectedNode, value))
                return;
            SelectedSetting = value as AppSettingsLeaf;
        }
    }

    private object? _selectedNode;
    private AppSettingsLeaf? _selectedSetting;

    public AppSettingsLeaf? SelectedSetting
    {
        get => _selectedSetting;
        private set
        {
            if (SetField(ref _selectedSetting, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
                OnPropertyChanged(nameof(IsModeLeaf));
                OnPropertyChanged(nameof(IsLanguageLeaf));
                OnPropertyChanged(nameof(IsModuleLeaf));
                OnPropertyChanged(nameof(SelectedModeLeaf));
                OnPropertyChanged(nameof(SelectedLanguageLeaf));
                OnPropertyChanged(nameof(SelectedModuleLeaf));
            }
        }
    }

    public bool HasSelection => SelectedSetting is not null;
    public bool HasNoSelection => SelectedSetting is null;

    // Editor-kind flags the detail pane binds IsVisible to (no converters needed).
    public bool IsModeLeaf => SelectedSetting is ModeSettingsLeaf;
    public bool IsLanguageLeaf => SelectedSetting is LanguageSettingsLeaf;
    public bool IsModuleLeaf => SelectedSetting is ModuleSettingsLeaf;

    // Typed selections so compiled XAML bindings (which are type-checked against the
    // property's declared type) can reach derived members — the bare SelectedSetting is
    // typed AppSettingsLeaf and cannot bind to leaf-specific properties.
    public ModeSettingsLeaf? SelectedModeLeaf => SelectedSetting as ModeSettingsLeaf;
    public LanguageSettingsLeaf? SelectedLanguageLeaf => SelectedSetting as LanguageSettingsLeaf;
    public ModuleSettingsLeaf? SelectedModuleLeaf => SelectedSetting as ModuleSettingsLeaf;

    /* ---------- Locale ---------- */

    private void OnLocaleChanged(object? sender, EventArgs e)
    {
        GlobalGroup.Relocalize(GroupName("global", "Global"), string.Empty);
        ModulesGroup.Relocalize(GroupName("modules", "Modules"), string.Empty);

        var mode = _globalLeaves.OfType<ModeSettingsLeaf>().FirstOrDefault();
        mode?.Relocalize(LeafName("mode", "Mode"),
            LeafDescription("mode", "Machine mode: Mill for CNC milling, Foam for hot-wire foam cutting."));
        var language = _globalLeaves.OfType<LanguageSettingsLeaf>().FirstOrDefault();
        language?.Relocalize(LeafName("language", "Language"),
            LeafDescription("language", "Interface language. Applied immediately."));

        foreach (var leaf in ModuleLeaves)
            if (_moduleById.TryGetValue(leaf.Id, out var module))
                leaf.Relocalize(ModuleName(module), ModuleDescription(module));
    }

    private void SetLocale(string code)
    {
        _currentLanguage = code;
        _settings.Language = code == "en" ? string.Empty : code;
        _localization.SetLocale(code);
        Save();
    }

    /* ---------- Mode / visibility ---------- */

    private void SetMode(string mode)
    {
        if (_settings.Mode == mode)
            return;
        _settings.Mode = mode;
        RefreshSectionVisibility();
        OnPropertyChanged(nameof(SelectedSetting));
        Save();
    }

    private bool IsModuleVisible(IModule m) =>
        _settings.Current.ModulesVisibility.GetValueOrDefault(m.Id, m.IsVisibleByDefault);

    private void VisibilityChanged(string id, bool value)
    {
        if (_updatingSections)
            return;
        _settings.Current.ModulesVisibility[id] = value;
        Save();
    }

    private void RefreshSectionVisibility()
    {
        _updatingSections = true;
        try
        {
            foreach (var module in ModuleLeaves)
            {
                var visible = _settings.Current.ModulesVisibility.GetValueOrDefault(
                    module.Id, _defaults.GetValueOrDefault(module.Id, true));
                if (module.CanHide && module.IsVisible != visible)
                    module.IsVisible = visible;
                module.RefreshFields();
            }
        }
        finally
        {
            _updatingSections = false;
        }
    }

    private void Save() => _settingsService.Save(_settings);

    protected void RequestClose(bool result) => CloseRequested?.Invoke(this, result);
}