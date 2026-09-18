/*
 * AppSettingsView.axaml.cs - App settings view
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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CNC.Core.ViewModels;

namespace SenderNG.Modules.Fixed;

public partial class AppSettingsView : UserControl
{
    public AppSettingsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private HotkeysSettingsLeaf? Leaf => (DataContext as AppSettingsViewModel)?.SelectedHotkeysLeaf;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var leaf = Leaf;
        if (leaf is null || !leaf.IsRecording)
            return;

        if (e.Key == Key.Escape)
        {
            leaf.CancelRecord();
            e.Handled = true;
            return;
        }

        if (AvaloniaHotkeyInterop.IsModifierKey(e.Key))
        {
            leaf.ReportModifierOnly();
            e.Handled = true;
            return;
        }

        if (!AvaloniaHotkeyInterop.TryGesture(e.Key, e.KeyModifiers, out var gesture) || gesture is null)
        {
            leaf.ReportInvalid();
            e.Handled = true;
            return;
        }

        leaf.ApplyRecordedGesture(gesture);
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var leaf = Leaf;
        if (leaf is null || !leaf.IsRecording)
            return;

        // Clicking the armed cell re-arms via its command; any other click cancels.
        if (e.Source is Control { } control && control.Classes.Contains("hotkey-cell"))
            return;
        leaf.CancelRecord();
    }
}
