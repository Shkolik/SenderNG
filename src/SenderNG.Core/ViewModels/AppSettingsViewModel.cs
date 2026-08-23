using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Core.Modules;

namespace CNC.Core.ViewModels;

/// <summary>One module row in the App Settings dialog: name + visibility checkbox.</summary>
public sealed class ModuleSettingsEntry : ViewModelBase
{
    private bool _isVisible;

    public ModuleSettingsEntry(string id, string name, bool isVisible, System.Action<string, bool> visibilityChanged)
    {
        ArgumentNullException.ThrowIfNull(visibilityChanged);
        Id = id;
        Name = name;
        _isVisible = isVisible;
        _visibilityChanged = visibilityChanged;
    }

    private readonly System.Action<string, bool> _visibilityChanged;

    public string Id { get; }

    public string Name { get; }

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

public class AppSettingsViewModel : ViewModelBase, IClosableDialog
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly Dictionary<string, bool> _defaults = new();
    private string _mode;
    private bool _updatingSections;

    public event EventHandler<bool>? CloseRequested;

    public AppSettingsViewModel(SettingsService settingsService, IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(modules);

        _settingsService = settingsService;
        _settings = settingsService.Load();
        _mode = _settings.Mode == AppSettings.FoamMode ? AppSettings.FoamMode : AppSettings.MillMode;

        foreach (var module in modules.OrderBy(m => m.Order))
        {
            _defaults[module.Id] = module.IsVisibleByDefault;
            ModuleSections.Add(new ModuleSettingsEntry(
                module.Id,
                module.Name,
                IsModuleVisible(module),
                VisibilityChanged));
        }

        CloseCommand = new DelegateCommand(_ => RequestClose(true));
    }

    public ObservableCollection<ModuleSettingsEntry> ModuleSections { get; } = new();

    public DelegateCommand CloseCommand { get; }

    // Mode radios write straight through to the shared settings graph: mode is a
    // global (not per-mode) setting and switching re-reads that mode's sections.
    public bool IsMill
    {
        get => _mode == AppSettings.MillMode;
        set => SetMode(value ? AppSettings.MillMode : _mode);
    }

    public bool IsFoam
    {
        get => _mode == AppSettings.FoamMode;
        set => SetMode(value ? AppSettings.FoamMode : _mode);
    }

    private bool IsModuleVisible(IModule m) =>
        _settings.Current.ModulesVisibility.GetValueOrDefault(m.Id, m.IsVisibleByDefault);

    private bool IsEntryVisible(string id) =>
        _settings.Current.ModulesVisibility.GetValueOrDefault(id, _defaults.GetValueOrDefault(id, true));

    private void VisibilityChanged(string id, bool value)
    {
        if (_updatingSections)
            return;
        _settings.Current.ModulesVisibility[id] = value;
        Save();
    }

    private void SetMode(string mode)
    {
        if (_mode == mode)
            return;
        _mode = mode;
        _settings.Mode = mode;
        RefreshSectionVisibility();
        OnPropertyChanged(nameof(IsMill));
        OnPropertyChanged(nameof(IsFoam));
        Save();
    }

    // Re-read each entry's visibility from the newly selected mode's map.
    private void RefreshSectionVisibility()
    {
        _updatingSections = true;
        try
        {
            foreach (var entry in ModuleSections)
            {
                var visible = IsEntryVisible(entry.Id);
                if (entry.IsVisible != visible)
                    entry.IsVisible = visible;
            }
        }
        finally
        {
            _updatingSections = false;
        }
    }

    private void Save() =>
        _settingsService.Save(_settings);

    protected void RequestClose(bool result) =>
        CloseRequested?.Invoke(this, result);
}
