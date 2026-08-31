
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

using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SenderNG.Modules.Plugins;

public partial class ProbeView : UserControl
{
    // Letters and other non-numeric characters are blocked in the probing
    // numeric inputs (left column + tool panels) so the double/int bindings
    // never hit a validation error. The editable profile ComboBox hosts a
    // TextBox that must keep accepting arbitrary text, so it is excluded.
    // The bindings also use NumericTextConverter, which silently ignores any
    // remaining unparseable text (dangling sign, empty field, paste), so no
    // red validation border or conversion exception can ever surface.
    private static readonly Regex DecimalPattern = new(@"^[+-]?\d*[.,]?\d*$", RegexOptions.Compiled);
    private static readonly Regex IntPattern = new(@"^[+-]?\d*$", RegexOptions.Compiled);

    public ProbeView()
    {
        InitializeComponent();
        AddHandler(InputElement.TextInputEvent, OnNumericTextInput, RoutingStrategies.Tunnel);
    }

    private void OnNumericTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is not TextBox box || string.IsNullOrEmpty(e.Text))
            return;

        // The editable profile ComboBox contains a TextBox for profile names.
        if (box.FindAncestorOfType<ComboBox>() is not null)
            return;

        var pattern = box.Classes.Contains("int") ? IntPattern : DecimalPattern;
        var text = box.Text ?? string.Empty;
        var caret = Math.Min(box.SelectionStart, box.SelectionEnd);
        var length = Math.Abs(box.SelectionEnd - box.SelectionStart);
        var result = text.Remove(caret, length).Insert(caret, e.Text);
        if (!pattern.IsMatch(result))
            e.Handled = true;
    }

    // Avalonia does not auto-open a Button's ContextMenu on click (WPF did);
    // open it explicitly from the "..." profile-actions button.
    private void ProfileMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Open(button);
        }
    }
}
