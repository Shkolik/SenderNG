using System;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;

namespace CNC.Core.ViewModels;

public class DemoMachineSettingsViewModel : ViewModelBase, IClosableDialog
{
    private const int MinAxes = 1;

    private readonly AppSettings _settings;
    private readonly DemoGrblService _demo;
    private readonly System.Action? _savedCallback;
    private readonly LocalizationService? _localization;
    private int _numAxes;
    private string _mode;

    public event EventHandler<bool>? CloseRequested;

    public DemoMachineSettingsViewModel(AppSettings settings, DemoGrblService demo, System.Action? savedCallback = null,
        LocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(demo);

        _settings = settings;
        _demo = demo;
        _savedCallback = savedCallback;
        _localization = localization ?? LocalizationService.Current;
        _numAxes = Math.Clamp(settings.Current.DemoAxes, MinAxes, GrblInfo.AxisLetters.Length);
        _mode = settings.Mode == "Foam" ? "Foam" : "Mill";

        OkCommand = new DelegateCommand(_ => Save());
        CancelCommand = new DelegateCommand(_ => RequestClose(false));
    }

    public DelegateCommand OkCommand { get; }

    public DelegateCommand CancelCommand { get; }

    public int NumAxes
    {
        get => _numAxes;
        set
        {
            if (_numAxes != value)
            {
                _numAxes = value;
                OnPropertyChanged(nameof(NumAxes));
            }
        }
    }

    // Local mode copy: written back to settings only on OK so Cancel leaves
    // the shared settings graph untouched.
    public bool IsMill
    {
        get => _mode == "Mill";
        set => SetMode(value ? "Mill" : _mode);
    }

    public bool IsFoam
    {
        get => _mode == "Foam";
        set => SetMode(value ? "Foam" : _mode);
    }

    protected void RequestClose(bool result) =>
        CloseRequested?.Invoke(this, result);

    private void SetMode(string mode)
    {
        if (_mode == mode)
            return;
        _mode = mode;
        OnPropertyChanged(nameof(IsMill));
        OnPropertyChanged(nameof(IsFoam));
    }

    private void Save()
    {
        var clamped = Math.Clamp(NumAxes, MinAxes, GrblInfo.AxisLetters.Length);
        if (clamped != NumAxes)
            NumAxes = clamped;

        // Mode first so the axes land in the newly selected mode's section.
        _settings.Mode = _mode;
        _settings.Current.DemoAxes = clamped;
        _demo.SetNumAxes(clamped);

        RequestClose(true);
        _savedCallback?.Invoke();
    }
}
