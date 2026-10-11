/*
 * BindingCommit.cs - commit a TextBox's pending text into its binding source
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
using Avalonia.Data;
using Avalonia.Input;

namespace SenderNG.Services;

/// <summary>Commits a focused TextBox's pending text into its binding source on
/// demand. Backs the "save on lost focus OR Enter" cadence: the bindings carry
/// <c>UpdateSourceTrigger=LostFocus</c>; these two entry points cover Enter
/// presses and window shutdown.</summary>
public static class BindingCommit
{
    /// <summary>KeyDown handler for view roots: Enter pushes the focused
    /// single-line TextBox's text into the source while it keeps focus. The
    /// event is never marked handled; AcceptsReturn boxes (macro code) are left
    /// to their LostFocus commit so a newline is not a save trigger.</summary>
    public static void OnEnterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Enter || e.Source is not TextBox { AcceptsReturn: false } tb)
            return;
        BindingOperations.GetBindingExpressionBase(tb, TextBox.TextProperty)?.UpdateSource();
    }

    /// <summary>Called from Window.Closing so a focused TextBox's uncommitted
    /// text reaches the source (and is persisted) before the window goes away.</summary>
    public static void CommitFocused(TopLevel level)
    {
        if (level.FocusManager?.GetFocusedElement() is TextBox tb)
            BindingOperations.GetBindingExpressionBase(tb, TextBox.TextProperty)?.UpdateSource();
    }
}
