
/*

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
