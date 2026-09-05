/*
 * TransformProgramViewModels.cs - File → Transform dialogs
 */

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
using SenderNG.Core.Commands;

namespace CNC.Core.ViewModels;

/// <summary>
/// Rotate dialog: angle in degrees + rotation origin. The origin pre-fills
/// with the current DRO (work) position when connected, else (0, 0) — the
/// values are editable either way, and applying is always explicit (OK).
/// </summary>
public class RotateProgramViewModel : ViewModelBase, IClosableDialog
{
    private double _angle = 90d;
    private double _originX;
    private double _originY;

    public RotateProgramViewModel(double originX = 0d, double originY = 0d)
    {
        _originX = originX;
        _originY = originY;
        OkCommand = new DelegateCommand(_ => RequestClose(true));
        CancelCommand = new DelegateCommand(_ => RequestClose(false));
    }

    public event EventHandler<bool>? CloseRequested;

    public double Angle
    {
        get => _angle;
        set => SetField(ref _angle, value);
    }

    public double OriginX
    {
        get => _originX;
        set => SetField(ref _originX, value);
    }

    public double OriginY
    {
        get => _originY;
        set => SetField(ref _originY, value);
    }

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    private void RequestClose(bool result) => CloseRequested?.Invoke(this, result);
}

/// <summary>
/// Arcs to lines dialog: chord tolerance in mm (the engine's resolution
/// convention — values &gt; 1 are treated as points-per-circle).
/// </summary>
public class ArcsToLinesProgramViewModel : ViewModelBase, IClosableDialog
{
    private double _tolerance = 0.1d;

    public ArcsToLinesProgramViewModel(double tolerance = 0.1d)
    {
        _tolerance = tolerance;
        OkCommand = new DelegateCommand(_ => RequestClose(true));
        CancelCommand = new DelegateCommand(_ => RequestClose(false));
    }

    public event EventHandler<bool>? CloseRequested;

    public double Tolerance
    {
        get => _tolerance;
        set => SetField(ref _tolerance, value);
    }

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    private void RequestClose(bool result) => CloseRequested?.Invoke(this, result);
}