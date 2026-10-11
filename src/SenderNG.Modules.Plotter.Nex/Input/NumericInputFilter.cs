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
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace SenderNG.Modules.Plotter.Nex.Input;

/// <summary>
/// Rejects characters that can never parse in a numeric TextBox, before they
/// appear in the box. Opt-in via CSS classes on each box: "num" (signed
/// decimal), "udec" (unsigned decimal), "int" (unsigned digits). Paste is
/// filtered too: an invalid clipboard payload is dropped instead of shown.
/// Unparseable text that still slips through (culture mismatch, races) is
/// neutralized at commit time by NumericTextConverter's DoNothing backstop.
/// </summary>
public static class NumericInputFilter
{
    private static readonly Regex NumPattern = new(@"^[+-]?\d*[.,]?\d*$", RegexOptions.Compiled);
    private static readonly Regex UdecPattern = new(@"^\d*[.,]?\d*$", RegexOptions.Compiled);
    private static readonly Regex IntPattern = new(@"^\d*$", RegexOptions.Compiled);

    public static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is not TextBox box || string.IsNullOrEmpty(e.Text))
            return;

        var pattern = PatternFor(box);
        if (pattern is null)
            return;

        var result = SimulateInsert(box.Text, box.SelectionStart, box.SelectionEnd, e.Text);
        if (!pattern.IsMatch(result))
            e.Handled = true;
    }

    public static void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox box || PatternFor(box) is null)
            return;

        // TextBox.Paste raises this event before reading the clipboard, then
        // calls HandleTextInput directly — bypassing the TextInputEvent tunnel
        // filter above. Stop its flow and re-implement: read the clipboard,
        // drop payloads that would not survive the same pattern gate, apply
        // valid ones through SelectedText (normal text machinery; the
        // LostFocus commit cadence still defers any save).
        e.Handled = true;
        _ = PasteAsync(box);
    }

    private static async Task PasteAsync(TextBox box)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(box);
            if (topLevel?.Clipboard is not { } clipboard)
                return;

            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrEmpty(text))
                return;

            var pattern = PatternFor(box);
            if (pattern is null)
                return;

            var result = SimulateInsert(box.Text, box.SelectionStart, box.SelectionEnd, text);
            if (pattern.IsMatch(result))
                box.SelectedText = text;
        }
        catch
        {
            // Clipboard unavailable or read failed — paste is dropped silently,
            // same as an invalid payload.
        }
    }

    private static Regex? PatternFor(TextBox box) =>
        box.Classes.Contains("int") ? IntPattern :
        box.Classes.Contains("udec") ? UdecPattern :
        box.Classes.Contains("num") ? NumPattern :
        null;

    private static string SimulateInsert(string? text, int selectionStart, int selectionEnd, string insert)
    {
        var current = text ?? string.Empty;
        var caret = Math.Min(selectionStart, selectionEnd);
        var length = Math.Abs(selectionEnd - selectionStart);
        return current.Remove(caret, length).Insert(caret, insert);
    }
}
