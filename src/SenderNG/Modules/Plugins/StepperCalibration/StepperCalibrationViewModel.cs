/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Globalization;
using CNC.Core;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;
using SenderNG.Core.Models;
using SenderNG.Core.Modules;
using SenderNG.Core.Settings;
using SenderNG.ViewModels;
using ViewModelBase = SenderNG.ViewModels.ViewModelBase;

namespace SenderNG.Modules.Plugins;

public enum StepperCalibrationStep { Setup, MeasureForward, MeasureBacklash, Done }

/// <summary>
/// Stepper calibration wizard: measures the real distance travelled for a commanded
/// move and solves the board's steps/mm. All logic lives here (no Core runner); the
/// resolution formula and G21G91G0 move pattern derive from Io Engineering's BSD-3
/// StepperCalibrationWizard, with a measured backlash step replacing its hardcoded
/// 0.5 mm overshoot.
/// </summary>
public sealed class StepperCalibrationViewModel : ViewModelBase, IAsyncDialogClosing
{
    private const int StepsBase = 100;
    private const int BacklashBase = 160;
    private const int MaxTravelBase = 130;
    private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(120);

    private readonly IGrblService _grbl;
    private readonly GrblInfoState _info;
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly GrblSettingsLoader _loader;

    private GrblSettingsSnapshot? _snapshot;
    private ModalState? _modalOnOpen;
    private bool _aborted;

    public StepperCalibrationViewModel(IGrblService grbl, IGrblSettingsProvider provider,
        GrblInfoState info, SettingsService settings, LocalizationService localization)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _loader = new GrblSettingsLoader(provider ?? throw new ArgumentNullException(nameof(provider)), localization);

        var count = Math.Clamp(_info.NumAxes, 1, _info.AxisLetters.Length);
        Axes = _info.AxisLetters[..count].Select(c => c.ToString()).ToList();
        Distance = StepperCalibrationSettingsStore.Load(_settings.Load()).Distance;

        NextCommand = new DelegateCommand(_ => _ = NextAsync(), _ => CanAdvance);
        BackCommand = new DelegateCommand(_ => _ = BackAsync(), _ => CanGoBack);
        SaveCommand = new DelegateCommand(_ => _ = SaveAsync(), _ => CanSave);
        StopCommand = new DelegateCommand(_ => _ = StopAsync(), _ => IsMoving);
        NewCommand = new DelegateCommand(_ => NewCalibration(), _ => IsDone);

        _ = InitializeAsync();
    }

    public IReadOnlyList<string> Axes { get; }
    public bool IsGrblHal => _info.IsGrblHAL;

    private int _selectedAxis;
    public int SelectedAxis
    {
        get => _selectedAxis;
        set
        {
            if (SetField(ref _selectedAxis, Math.Clamp(value, 0, Math.Max(0, Axes.Count - 1))))
            {
                OnPropertyChanged(nameof(SelectedAxisLetter));
                LoadAxisSettings();
            }
        }
    }

    public string SelectedAxisLetter =>
        SelectedAxis >= 0 && SelectedAxis < Axes.Count ? Axes[SelectedAxis] : "X";

    private bool _movePositive = true;
    public bool MovePositive { get => _movePositive; set => SetField(ref _movePositive, value); }

    private double _distance = 100;
    public double Distance
    {
        get => _distance;
        set
        {
            if (SetField(ref _distance, value))
            {
                MeasuredDistance = null;
                Backlash = null;
                Recompute();
            }
        }
    }

    private double? _measured;
    public double? MeasuredDistance
    {
        get => _measured;
        set { if (SetField(ref _measured, value)) Recompute(); }
    }

    private double? _backlash;
    public double? Backlash
    {
        get => _backlash;
        set { if (SetField(ref _backlash, value)) RaiseCommands(); }
    }

    private double? _newSteps;
    public double? NewStepsPerUnit { get => _newSteps; private set => SetField(ref _newSteps, value); }

    private double? _currentSteps;
    public double? CurrentStepsPerUnit { get => _currentSteps; private set => SetField(ref _currentSteps, value); }

    private bool _backlashSupported;
    public bool BacklashSupported { get => _backlashSupported; private set => SetField(ref _backlashSupported, value); }

    private string _status = string.Empty;
    public string Status { get => _status; private set => SetField(ref _status, value); }

    private string _resultText = string.Empty;
    public string ResultText { get => _resultText; private set => SetField(ref _resultText, value); }

    private StepperCalibrationStep _step = StepperCalibrationStep.Setup;
    public StepperCalibrationStep Step
    {
        get => _step;
        private set
        {
            if (SetField(ref _step, value))
            {
                OnPropertyChanged(nameof(IsSetup));
                OnPropertyChanged(nameof(IsMeasure));
                OnPropertyChanged(nameof(IsDone));
                RaiseCommands();
            }
        }
    }

    public bool IsSetup => Step == StepperCalibrationStep.Setup;
    public bool IsMeasure => Step is StepperCalibrationStep.MeasureForward or StepperCalibrationStep.MeasureBacklash;
    public bool IsDone => Step == StepperCalibrationStep.Done;

    private bool _moving;
    public bool IsMoving { get => _moving; private set { if (SetField(ref _moving, value)) RaiseCommands(); } }

    public DelegateCommand NextCommand { get; }
    public DelegateCommand BackCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand NewCommand { get; }

    public bool CanAdvance => !IsMoving &&
        Step is StepperCalibrationStep.Setup or StepperCalibrationStep.MeasureForward;

    public bool CanGoBack => !IsMoving &&
        Step is StepperCalibrationStep.MeasureForward or StepperCalibrationStep.MeasureBacklash;

    public bool CanSave => !IsMoving && Step == StepperCalibrationStep.MeasureBacklash &&
        Backlash is >= 0;

    private void RaiseCommands()
    {
        NextCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        NewCommand.RaiseCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        try
        {
            _snapshot = await _loader.LoadAsync();
        }
        catch (Exception ex)
        {
            Status = Format("module.stepper-calibration.loadFailed", "Load failed: {0}", ex.Message);
        }
        try
        {
            _modalOnOpen = await _grbl.ReadModalStateAsync();
        }
        catch
        {
            _modalOnOpen = null;
        }
        LoadAxisSettings();
    }

    private void LoadAxisSettings()
    {
        CurrentStepsPerUnit = ReadSetting(StepsBase + SelectedAxis);
        BacklashSupported = ReadSetting(BacklashBase + SelectedAxis) is not null;
        Recompute();
    }

    public void Recompute()
    {
        NewStepsPerUnit = CurrentStepsPerUnit is { } current && MeasuredDistance is { } measured &&
                          measured > 0 && Distance > 0
            ? Math.Round(current * Distance / measured, IsGrblHal ? 6 : 3)
            : null;
        OnPropertyChanged(nameof(CanSave));
        RaiseCommands();
    }

    private double? ReadSetting(int id)
    {
        if (_snapshot?.Settings.FirstOrDefault(s => s.Id == id) is not { } setting)
            return null;
        return double.TryParse(setting.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public async Task NextAsync()
    {
        if (!CanAdvance)
            return;
        if (Step == StepperCalibrationStep.Setup)
        {
            if (Distance <= 0)
            {
                Status = Resolve("module.stepper-calibration.invalidDistance", "Enter a distance greater than 0.");
                return;
            }
            var (ok, warning) = await MoveAsync(MovePositive ? 1 : -1, Distance);
            if (!ok)
                return;
            Step = StepperCalibrationStep.MeasureForward;
            Status = Compose(Resolve("module.stepper-calibration.measureForward",
                "Measure the distance the axis moved, then enter it."), warning);
        }
        else if (Step == StepperCalibrationStep.MeasureForward)
        {
            if (MeasuredDistance is not > 0)
            {
                Status = Resolve("module.stepper-calibration.invalidMeasured", "Enter the measured distance.");
                return;
            }
            var (ok, warning) = await MoveAsync(MovePositive ? -1 : 1, Distance);
            if (!ok)
                return;
            Step = StepperCalibrationStep.MeasureBacklash;
            Status = Compose(Resolve("module.stepper-calibration.measureBacklash",
                "Enter how far short of the start the axis returned (0 = exact)."), warning);
        }
    }

    public Task BackAsync()
    {
        if (!CanGoBack)
            return Task.CompletedTask;
        Step = Step == StepperCalibrationStep.MeasureBacklash
            ? StepperCalibrationStep.MeasureForward
            : StepperCalibrationStep.Setup;
        return Task.CompletedTask;
    }

    // Returns the warn-only space-check notice (if any) so the caller can keep it
    // visible next to the next step's instruction. Async methods can't use `out`.
    private async Task<(bool Ok, string? Warning)> MoveAsync(int direction, double distance)
    {
        _aborted = false;
        var index = SelectedAxis;
        var landing = CurrentPosition(index) + direction * distance;
        if (!CheckSpace(index, landing, out var message))
        {
            Status = message!;
            return (false, null);
        }

        IsMoving = true;
        try
        {
            // Arm the idle gate BEFORE the move: subscribing after the send would
            // replay the pre-move "Idle" status (GrblServiceProxy/DemoGrblService
            // expose a BehaviorSubject) and could let the wizard advance while the
            // axis is still travelling. The gate drops that replay and waits for a
            // non-Idle observation before accepting Idle.
            var idleTask = WaitForIdleAsync();
            await _grbl.SendAsync(
                $"G21G91G0{SelectedAxisLetter}{(direction * distance).ToString(CultureInfo.InvariantCulture)}");
            var idle = await idleTask;
            if (_aborted)
                return (false, null);
            if (!idle)
            {
                Status = Resolve("module.stepper-calibration.moveAborted",
                    "Move did not complete (alarm or timeout).");
                return (false, null);
            }
            return (true, message);
        }
        catch (Exception ex)
        {
            Status = Format("module.stepper-calibration.moveFailed", "Move failed: {0}", ex.Message);
            return (false, null);
        }
        finally
        {
            IsMoving = false;
        }
    }

    private bool CheckSpace(int index, double landing, out string? message)
    {
        message = null;
        var travel = ReadSetting(MaxTravelBase + index);
        var homed = LatestState()?.IsHomed ?? false;
        if (travel is null or <= 0 || !homed)
        {
            message = Format("module.stepper-calibration.travelUnknown",
                "{0}: travel unknown, space check skipped.", SelectedAxisLetter);
            return true; // warn only
        }
        if (landing < 0 || landing > travel)
        {
            message = Format("module.stepper-calibration.outOfRange",
                "{0} move to {1:0.###} mm exceeds ${2}={3:0.###} mm",
                SelectedAxisLetter, landing, MaxTravelBase + index, travel.Value);
            return false;
        }
        return true;
    }

    private double CurrentPosition(int index)
    {
        var state = LatestState();
        if (state is null)
            return 0;
        if (state.IsHomed && state.MachinePositions is { Length: > 0 } machine && index < machine.Length)
            return machine[index];
        return index < state.Positions.Length ? state.Positions[index] : 0;
    }

    private MachineState? LatestState()
    {
        MachineState? state = null;
        using var sub = _grbl.State.Subscribe(s => state = s);
        return state;
    }

    // Completes when the controller reports Idle again after the move, or (false) on
// an Alarm or a 120s timeout. Callers must invoke this BEFORE issuing the move:
// the first emission this subscription receives is the BehaviorSubject replay of
// the last state — which, if subscribed after the send, is the pre-move "Idle"
// that makes the wait succeed while the axis is still moving. The replay is
// dropped, and Idle is accepted only after a non-Idle state was actually observed:
// a poll that answers Idle before the move physically starts (it can beat the G0
// line to the wire) must not count as completion either.
    private async Task<bool> WaitForIdleAsync()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replay = true;
        var busy = false;
        using var sub = _grbl.State.Subscribe(s =>
        {
            if (replay)
            {
                replay = false;   // drop the subscribe-time replay of the last state
                return;
            }
            var status = s?.Status ?? string.Empty;
            if (status.Contains("alarm", StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(false);
            else if (status.Contains("idle", StringComparison.OrdinalIgnoreCase))
            {
                if (busy)
                    tcs.TrySetResult(true);
            }
            else
                busy = true;
        });
        using var cts = new CancellationTokenSource(MoveTimeout);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task StopAsync()
    {
        var wasMoving = IsMoving;
        _aborted = true;
        if (wasMoving)
        {
            try { _grbl.SendRealtime(GrblRealtime.StopByte(IsGrblHal)); } catch { }
        }
        IsMoving = false;
        await RestoreModalAsync(refreshAfterStop: IsGrblHal && wasMoving);
        Step = StepperCalibrationStep.Setup;
        Status = Resolve("module.stepper-calibration.aborted", "Calibration cancelled; nothing was saved.");
    }

    // Closing the dialog mid-wizard (window X, Esc) leaves G91/G20 modal state on
    // the controller unless we abort and restore the open-time snapshot here.
    public async Task OnDialogClosedAsync() => await StopAsync();

    public async Task SaveAsync()
    {
        if (!CanSave)
            return;
        try
        {
            var newSteps = NewStepsPerUnit ?? CurrentStepsPerUnit;
            if (newSteps is { } steps)
                await _grbl.SendAsync(
                    $"${StepsBase + SelectedAxis}={steps.ToString(CultureInfo.InvariantCulture)}");

            BacklashSupported = ReadSetting(BacklashBase + SelectedAxis) is not null;
            if (BacklashSupported && Backlash is { } backlash)
                await _grbl.SendAsync(
                    $"${BacklashBase + SelectedAxis}={backlash.ToString(CultureInfo.InvariantCulture)}");

            var profile = _settings.Load();
            StepperCalibrationSettingsStore.Save(profile, new StepperCalibrationSettings { Distance = Distance });
            _settings.Save(profile);

            await RestoreModalAsync(refreshAfterStop: false);

            ResultText = BacklashSupported
                ? Format("module.stepper-calibration.savedWithBacklash",
                    "Steps/mm: {0:0.###} → {1:0.###}; backlash {2:0.###} mm saved.",
                    CurrentStepsPerUnit ?? 0, newSteps ?? 0, Backlash ?? 0)
                : Format("module.stepper-calibration.savedNoBacklash",
                    "Steps/mm: {0:0.###} → {1:0.###}. Backlash compensation is not supported by this controller — informational only.",
                    CurrentStepsPerUnit ?? 0, newSteps ?? 0);
            Step = StepperCalibrationStep.Done;
            Status = Resolve("module.stepper-calibration.saved", "Calibration saved to the controller.");
        }
        catch (Exception ex)
        {
            Status = Format("module.stepper-calibration.saveFailed", "Save failed: {0}", ex.Message);
        }
    }

    private async Task RestoreModalAsync(bool refreshAfterStop)
    {
        var modal = _modalOnOpen;
        if (refreshAfterStop)
        {
            try
            {
                if (await _grbl.ReadModalStateAsync() is { } live)
                    modal = live;
            }
            catch
            {
                // keep the open-time snapshot
            }
        }
        if (modal is null)
            return;
        try
        {
            await _grbl.SendAsync(modal.IsMetric ? "G21" : "G20");
            await _grbl.SendAsync(modal.IsAbsolute ? "G90" : "G91");
        }
        catch
        {
            // non-fatal
        }
    }

    public void NewCalibration()
    {
        if (!IsDone)
            return;
        MeasuredDistance = null;
        Backlash = null;
        ResultText = string.Empty;
        Step = StepperCalibrationStep.Setup;
        Status = Resolve("module.stepper-calibration.restart", "Ready.");
    }

    private static string Compose(string status, string? warning) =>
        warning is null ? status : $"{status} {warning}";

    private string Resolve(string key, string fallback) => _localization.T(key) is var t && t != key ? t : fallback;

    private string Format(string key, string fallback, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Resolve(key, fallback), args);
}