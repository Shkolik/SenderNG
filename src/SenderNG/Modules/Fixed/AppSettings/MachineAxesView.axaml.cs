/*
 * MachineAxesView.axaml.cs - machine card axis editor
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
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CNC.Core.ViewModels;

namespace SenderNG.Modules.Fixed;

/// <summary>
/// The machine card's axis editor view: one row per axis grouped by plane and the
/// optional-axis list. The machine-type topology picture lives beside the profile list
/// (bound through <c>Converters.MachineTypeImage</c>), not here.
/// <para>
/// The editors are <c>OneWay</c>-bound and write only from the explicit, type-guarded,
/// deferred handlers below. The rows live in recycled <see cref="ItemsControl"/>
/// containers (a profile switch, a machine-type switch, a row removal or a kind change all
/// reuse them), and a recycled control raises its Change event with the previous row's
/// value already pointed at the new row — so a handler that wrote blindly would corrupt
/// the just-selected axis. See <c>docs/avalonia-gotchas.md</c> §1.
/// </para>
/// </summary>
public partial class MachineAxesView : UserControl
{
    public MachineAxesView()
    {
        InitializeComponent();
    }

    /* -----------------------------------------------------------------------
     * Editor events
     *
     * Every handler below does the same two things, and must keep doing them:
     *   1. Guard by editor type — the control's *current* DataContext has to be the
     *      row (or pivot) kind this editor belongs to. A recycled event whose control
     *      now belongs to a row that does not use this editor is dropped.
     *   2. Defer the write to the input queue, so the OneWay binding has re-synced the
     *      control to its row first. After that a recycled or programmatic change
     *      carries the row's OWN value and writes nothing; only a real gesture still
     *      differs from the model.
     * The row's Apply* methods no-op on an unchanged value, which is the final backstop.
     * --------------------------------------------------------------------- */

    private static void OnIndexSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (combo.DataContext is MachineAxisRowViewModel row
                && combo.SelectedItem is AxisIndexChoice choice
                && choice.Index != row.Index)
            {
                row.ApplyIndex(choice.Index);
            }
        }, DispatcherPriority.Input);
    }

    private static void OnKindSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (combo.DataContext is MachineAxisRowViewModel row
                && combo.SelectedItem is AxisKindChoice choice
                && choice.Value != row.Kind)
            {
                row.ApplyKind(choice.Value);
            }
        }, DispatcherPriority.Input);
    }

    private static void OnDirectionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (combo.DataContext is MachineAxisRowViewModel row
                && combo.SelectedItem is AxisDirectionChoice choice
                && choice.Value != row.Direction)
            {
                row.ApplyDirection(choice.Value);
            }
        }, DispatcherPriority.Input);
    }

    /// <summary>Sign is the Direction column's checkbox, driven by a Click, not by
    /// IsCheckedChanged: the state is captured from the gesture and committed through the
    /// deferred, DataContext-guarded shape the combo handlers use, and a recycled container
    /// whose DataContext has moved on writes nothing.</summary>
    private static void OnInvertedClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not MachineAxisRowViewModel row)
            return;
        var inverted = box.IsChecked == true;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(box.DataContext, row))
                row.SetInverted(inverted);
        }, DispatcherPriority.Input);
    }

    private static void OnAliasTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (box.DataContext is MachineAxisRowViewModel row && (box.Text ?? string.Empty) != row.AliasText)
                row.ApplyAlias(box.Text);
        }, DispatcherPriority.Input);
    }

    private static void OnPivotTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            // Guard by editor type: only a rotary row has pivot inputs, and the
            // container may already have been recycled onto a linear row.
            if (box.DataContext is AxisPivotRowViewModel pivot
                && (box.Text ?? string.Empty) != pivot.ValueText
                && MachineAxesViewModel.TryParseNumber(box.Text, out var value))
            {
                pivot.ApplyValue(value);
            }
        }, DispatcherPriority.Input);
    }

    private static void OnSeparationTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (box.DataContext is MachineAxesViewModel vm
                && (box.Text ?? string.Empty) != vm.SeparationText
                && MachineAxesViewModel.TryParseNumber(box.Text, out var value))
            {
                vm.Separation = value;
            }
        }, DispatcherPriority.Input);
    }
}
