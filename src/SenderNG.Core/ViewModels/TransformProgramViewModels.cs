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
using System.Collections.Generic;
using System.Linq;
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

/// <summary>Direction of the rotary wrap transform.</summary>
public enum WrapDirection
{
    /// <summary>Linear toolpath → rotary axis (X/Y → A/B).</summary>
    Wrap,
    /// <summary>Rotary toolpath → linear axis (A/B → X/Y).</summary>
    Unwrap,
}

/// <summary>A selectable axis in the wrap/unwrap dialog (index + display label).</summary>
public sealed class WrapAxisOption
{
    public WrapAxisOption(int index, string label)
    {
        Index = index;
        Label = label;
    }

    public int Index { get; }
    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>
/// Wrap / Unwrap dialog: source and target axis (filtered by direction and the
/// controller's axis count), the cylinder diameter and the Z0-at-center option.
/// Wrap and Unwrap share this model; only the option lists and defaults differ.
/// </summary>
public class WrapProgramViewModel : ViewModelBase, IClosableDialog
{
    private readonly Func<string, string> _axisLabel;
    private WrapAxisOption? _sourceAxis;
    private WrapAxisOption? _targetAxis;
    private double _diameter = 50d;
    private bool _z0AtCenter;

    /// <param name="axisLabel">Formats one axis letter into the label shown in the
    /// dialog. Supplied by the UI (this assembly cannot reach the app's
    /// localization service); the default keeps the tests and any non-localized
    /// host working.</param>
    public WrapProgramViewModel(WrapDirection direction, string axisLetters, int numAxes,
        Func<string, string>? axisLabel = null)
    {
        Direction = direction;
        _axisLabel = axisLabel ?? (letter => letter + "-axis");
        var wrap = direction == WrapDirection.Wrap;

        SourceAxes = BuildAxes(linear: wrap, axisLetters, numAxes);
        TargetAxes = BuildAxes(linear: !wrap, axisLetters, numAxes);

        _sourceAxis = Pick(SourceAxes, wrap ? new[] { 1, 0 } : new[] { 3, 4 });
        _targetAxis = Pick(TargetAxes, wrap ? new[] { 3, 4 } : new[] { 0, 1 });

        OkCommand = new DelegateCommand(_ => RequestClose(true), _ => _diameter > 0d);
        CancelCommand = new DelegateCommand(_ => RequestClose(false));
    }

    public event EventHandler<bool>? CloseRequested;

    public WrapDirection Direction { get; }
    public bool IsWrap => Direction == WrapDirection.Wrap;

    public IReadOnlyList<WrapAxisOption> SourceAxes { get; }
    public IReadOnlyList<WrapAxisOption> TargetAxes { get; }

    public WrapAxisOption? SourceAxis
    {
        get => _sourceAxis;
        set => SetField(ref _sourceAxis, value);
    }

    public WrapAxisOption? TargetAxis
    {
        get => _targetAxis;
        set => SetField(ref _targetAxis, value);
    }

    /// <summary>Cylinder diameter in mm. A non-positive value disables OK —
    /// the transform is meaningless without a positive diameter, and silently
    /// ignoring the click would give the user no feedback.</summary>
    public double Diameter
    {
        get => _diameter;
        set
        {
            if (SetField(ref _diameter, value))
                (OkCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool Z0AtCenter
    {
        get => _z0AtCenter;
        set => SetField(ref _z0AtCenter, value);
    }

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    private void RequestClose(bool result) => CloseRequested?.Invoke(this, result);

    private IReadOnlyList<WrapAxisOption> BuildAxes(bool linear, string letters, int numAxes)
    {
        var count = Math.Clamp(numAxes, 0, letters.Length);
        var axes = new List<WrapAxisOption>();
        if (linear)
        {
            if (count > 0) axes.Add(new WrapAxisOption(0, _axisLabel(letters[0].ToString())));
            if (count > 1) axes.Add(new WrapAxisOption(1, _axisLabel(letters[1].ToString())));
        }
        else
        {
            if (count > 3) axes.Add(new WrapAxisOption(3, _axisLabel(letters[3].ToString())));
            if (count > 4) axes.Add(new WrapAxisOption(4, _axisLabel(letters[4].ToString())));
        }
        return axes;
    }

    private static WrapAxisOption? Pick(IReadOnlyList<WrapAxisOption> axes, int[] preferred)
    {
        foreach (var index in preferred)
            foreach (var axis in axes)
                if (axis.Index == index)
                    return axis;
        return axes.Count > 0 ? axes[0] : null;
    }
}