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

using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.ViewModels;
using SenderNG.Services;

namespace SenderNG.Modules.Fixed;

public partial class AppSettingsView : UserControl
{
    public AppSettingsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, BindingCommit.OnEnterKeyDown, RoutingStrategies.Bubble);
        AddHandler(InputElement.TextInputEvent, NumericInputFilter.OnTextInput, RoutingStrategies.Tunnel);
        AddHandler(TextBox.PastingFromClipboardEvent, NumericInputFilter.OnPasting, RoutingStrategies.Bubble);
    }

    private HotkeysSettingsLeaf? Leaf => (DataContext as AppSettingsViewModel)?.SelectedHotkeysLeaf;

    private ProfileSettingsLeaf? ProfilesLeaf => (DataContext as AppSettingsViewModel)?.SelectedProfileLeaf;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleHotkeyRecording(e))
            return;
        HandleProfileRenameKeys(e);
    }

    /// <summary>Hotkey capture owns the keyboard while a cell is armed.</summary>
    private bool HandleHotkeyRecording(KeyEventArgs e)
    {
        var leaf = Leaf;
        if (leaf is null || !leaf.IsRecording)
            return false;

        if (e.Key == Key.Escape)
        {
            leaf.CancelRecord();
            e.Handled = true;
            return true;
        }

        if (AvaloniaHotkeyInterop.IsModifierKey(e.Key))
        {
            leaf.ReportModifierOnly();
            e.Handled = true;
            return true;
        }

        if (!AvaloniaHotkeyInterop.TryGesture(e.Key, e.KeyModifiers, out var gesture) || gesture is null)
        {
            leaf.ReportInvalid();
            e.Handled = true;
            return true;
        }

        leaf.ApplyRecordedGesture(gesture);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Keyboard side of the in-list profile rename. F2 opens the selected row for
    /// editing; Escape drops the draft without writing. The edit box itself is left
    /// to its own KeyDown handler, so Enter there commits rather than depending on
    /// the binding's lost-focus cadence fire while the box still holds focus.
    /// </summary>
    private void HandleProfileRenameKeys(KeyEventArgs e)
    {
        var leaf = ProfilesLeaf;
        if (leaf is null || e.Handled)
            return;
        if (e.Key != Key.F2 || e.Source is TextBox)
            return;
        var selected = leaf.SelectedProfile;
        if (selected is null)
            return;
        leaf.BeginRename(selected);
        FocusRenameBox();
        e.Handled = true;
    }

    /// <summary>
    /// Keeps the draft live while typing (so the blank/duplicate warning appears
    /// immediately) and owns Enter and Escape for the row being renamed.
    /// </summary>
    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ProfileRow item } box)
            return;
        var leaf = ProfilesLeaf;
        if (leaf is null)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                leaf.UpdateRename(item, box.Text);
                leaf.CommitRename(item);
                // A refused commit (blank or duplicate name) leaves the row in edit mode
                // with the reason beside it — keep the caret in the box so the fix is one
                // keystroke away instead of a dead editor the operator has to re-open.
                if (item.IsEditing && !box.IsFocused)
                    box.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                leaf.CancelRename();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Double-clicking a row's name starts the rename (the pencil's keyboard
    /// equivalent is F2).</summary>
    private void OnProfileNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ProfileRow item })
            return;
        var leaf = ProfilesLeaf;
        if (leaf is null)
            return;
        leaf.BeginRename(item);
        FocusRenameBox();
        e.Handled = true;
    }

    /// <summary>The pencil starts the edit through the row's command, which is view-less,
    /// so it lands here to put the caret in the box with the name selected, exactly like
    /// the double-click and F2 paths. If the command refused the rename there is no
    /// visible box, and the focus call is a no-op.</summary>
    private void OnRenameInvoked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ProfileRow })
            FocusRenameBox();
    }

    /// <summary>The context menu's rename entry cannot land on a XAML Click handler: the
    /// MenuItem class handler runs the command and marks Click handled before instance
    /// handlers would run. The request therefore arrives here instead — a closed menu
    /// over a row that is now editing.</summary>
    private void OnRowMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { DataContext: ProfileRow { IsEditing: true } })
            FocusRenameBox();
    }

    /// <summary>Moves focus into the row's editor with the whole name selected. Posted,
    /// because on the button path the editor opens only after the click finishes
    /// bubbling, and a hidden box would silently refuse the focus call.</summary>
    private void FocusRenameBox() => Dispatcher.UIThread.Post(() =>
    {
        var box = this.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(t => t.Classes.Contains("rename") && t.IsVisible);
        if (box is null)
            return;
        box.Focus();
        box.SelectAll();
    }, DispatcherPriority.Loaded);

    /// <summary>Pull the draft in as it changes so the validation message is live.</summary>
    private void OnRenameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ProfileRow item } box)
            ProfilesLeaf?.UpdateRename(item, box.Text);
    }

    /// <summary>Lost focus persists the name — an edit abandoned by clicking elsewhere
    /// must not be silently discarded. Only the Enter path re-focuses on a refusal:
    /// stealing focus back here would fight whatever the operator just clicked.</summary>
    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ProfileRow item } box)
        {
            ProfilesLeaf?.UpdateRename(item, box.Text);
            ProfilesLeaf?.CommitRename(item);
        }
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
