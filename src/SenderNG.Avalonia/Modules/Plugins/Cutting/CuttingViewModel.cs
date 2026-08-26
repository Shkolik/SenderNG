using System.Reactive.Linq;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Plugins;

public class CuttingViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;

    private int _feedOvr = 100;
    public int FeedOvr
    {
        get => _feedOvr;
        private set => SetField(ref _feedOvr, value);
    }

    private int _rapidOvr = 100;
    public int RapidOvr
    {
        get => _rapidOvr;
        private set
        {
            if (SetField(ref _rapidOvr, value))
            {
                OnPropertyChanged(nameof(IsRapidFull));
                OnPropertyChanged(nameof(IsRapidHalf));
                OnPropertyChanged(nameof(IsRapidQuarter));
            }
        }
    }

    public bool IsRapidFull => RapidOvr == 100;
    public bool IsRapidHalf => RapidOvr == 50;
    public bool IsRapidQuarter => RapidOvr == 25;

    private int _spindleOvr = 100;
    public int SpindleOvr
    {
        get => _spindleOvr;
        private set
        {
            if (SetField(ref _spindleOvr, value))
            {
                OnPropertyChanged(nameof(Speed));
                OnPropertyChanged(nameof(SpindleRpm));
            }
        }
    }

    private double _feed;
    public double Feed
    {
        get => _feed;
        private set => SetField(ref _feed, value);
    }

    private double _rawSpeed;
    public double Speed
    {
        get => _rawSpeed * SpindleOvr / 100d;
        private set
        {
            if (SetField(ref _rawSpeed, value, nameof(Speed)))
            {
                OnPropertyChanged(nameof(IsSpindleOn));
                // Direction buttons only make sense while the spindle is stopped.
                SpindleCwCommand?.RaiseCanExecuteChanged();
                SpindleCcwCommand?.RaiseCanExecuteChanged();
                // Keep the last commanded S word when the spindle stops instead
                // of replacing the editable RPM with zero.
                if (value > 0)
                    SetCommandedSpindleRpm((int)Math.Round(value));
            }
        }
    }

    private int _commandedSpindleRpm = 1000;
    /// <summary>Effective spindle speed shown in the header and RPM input.</summary>
    public int SpindleRpm
    {
        get => (int)Math.Round(_commandedSpindleRpm * SpindleOvr / 100d);
        set
        {
            var commanded = SpindleOvr == 0
                ? value
                : (int)Math.Round(value * 100d / SpindleOvr);
            SetCommandedSpindleRpm(commanded);
        }
    }

    private void SetCommandedSpindleRpm(int value) =>
        SetField(ref _commandedSpindleRpm, value, nameof(SpindleRpm));

    /// <summary>Spindle is running (raw live speed above zero).</summary>
    public bool IsSpindleOn => _rawSpeed > 0;

    public DelegateCommand FeedResetCommand { get; }
    public DelegateCommand FeedCoarsePlusCommand { get; }
    public DelegateCommand FeedCoarseMinusCommand { get; }
    public DelegateCommand FeedFinePlusCommand { get; }
    public DelegateCommand FeedFineMinusCommand { get; }

    public DelegateCommand RapidFullCommand { get; }
    public DelegateCommand RapidHalfCommand { get; }
    public DelegateCommand RapidQuarterCommand { get; }

    public DelegateCommand SpeedResetCommand { get; }
    public DelegateCommand SpeedCoarsePlusCommand { get; }
    public DelegateCommand SpeedCoarseMinusCommand { get; }
    public DelegateCommand SpeedFinePlusCommand { get; }
    public DelegateCommand SpeedFineMinusCommand { get; }

    public DelegateCommand SpindleOffCommand { get; }

    public DelegateCommand SpindleCwCommand { get; }

    public DelegateCommand SpindleCcwCommand { get; }

    public CuttingViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));

        FeedResetCommand = new DelegateCommand(_ => Send(GrblRealtime.FeedOvrReset));
        FeedCoarsePlusCommand = new DelegateCommand(_ => Send(GrblRealtime.FeedOvrCoarsePlus));
        FeedCoarseMinusCommand = new DelegateCommand(_ => Send(GrblRealtime.FeedOvrCoarseMinus));
        FeedFinePlusCommand = new DelegateCommand(_ => Send(GrblRealtime.FeedOvrFinePlus));
        FeedFineMinusCommand = new DelegateCommand(_ => Send(GrblRealtime.FeedOvrFineMinus));

        RapidFullCommand = new DelegateCommand(_ => Send(GrblRealtime.RapidOvrReset));
        RapidHalfCommand = new DelegateCommand(_ => Send(GrblRealtime.RapidOvrMedium));
        RapidQuarterCommand = new DelegateCommand(_ => Send(GrblRealtime.RapidOvrLow));

        SpeedResetCommand = new DelegateCommand(_ => Send(GrblRealtime.SpindleOvrReset));
        SpeedCoarsePlusCommand = new DelegateCommand(_ => Send(GrblRealtime.SpindleOvrCoarsePlus));
        SpeedCoarseMinusCommand = new DelegateCommand(_ => Send(GrblRealtime.SpindleOvrCoarseMinus));
        SpeedFinePlusCommand = new DelegateCommand(_ => Send(GrblRealtime.SpindleOvrFinePlus));
        SpeedFineMinusCommand = new DelegateCommand(_ => Send(GrblRealtime.SpindleOvrFineMinus));

        // Off stops the spindle with a line command (ioSender: M5), never restarts it.
        SpindleOffCommand = new DelegateCommand(_ => SendLine("M5"));
        // CW/CCW start the spindle with the entered RPM (ioSender: M3/M4 + S value).
        SpindleCwCommand = new DelegateCommand(_ => StartSpindle("M3"), _ => !IsSpindleOn);
        SpindleCcwCommand = new DelegateCommand(_ => StartSpindle("M4"), _ => !IsSpindleOn);

        // Live values mirror the machine (FS:/Ov: report fields; demo simulation).
        grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
    }

    private void Send(byte code)
    {
        try
        {
            _grbl.SendRealtime(code);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cutting override failed: {ex.Message}");
        }
    }

    private void StartSpindle(string mode)
    {
        try
        {
            _ = _grbl.SendAsync($"{mode} S{_commandedSpindleRpm}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spindle start failed: {ex.Message}");
        }
    }

    private void SendLine(string line)
    {
        try
        {
            _ = _grbl.SendAsync(line);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spindle command failed: {ex.Message}");
        }
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;
        Feed = state.Feed;
        SpindleOvr = state.SpindleOvr;
        Speed = state.Speed;
        FeedOvr = state.FeedOvr;
        RapidOvr = state.RapidOvr;
    }
}
