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

namespace SenderNG.Modules.Plugins;

public partial class StepperCalibrationView : UserControl
{
    // The commanded distance is allowed to be negative (the axis moves the other
    // way), but the measured distance and backlash must stay non-negative: their
    // double? bindings would otherwise flash a red validation border while the
    // sign is typed. The regex blocks '-' (and any other malformed input) before
    // it reaches the NumericTextConverter, which silently swallows the rest.
    private static readonly Regex SignedDecimalPattern = new(@"^[+-]?\d*[.,]?\d*$", RegexOptions.Compiled);
    private static readonly Regex UnsignedDecimalPattern = new(@"^\d*[.,]?\d*$", RegexOptions.Compiled);

    public StepperCalibrationView()
    {
        InitializeComponent();
        AddHandler(InputElement.TextInputEvent, OnNumericTextInput, RoutingStrategies.Tunnel);
    }

    private void OnNumericTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is not TextBox box || string.IsNullOrEmpty(e.Text))
            return;

        var pattern = box.Name == "DistanceBox" ? SignedDecimalPattern : UnsignedDecimalPattern;
        var text = box.Text ?? string.Empty;
        var caret = Math.Min(box.SelectionStart, box.SelectionEnd);
        var length = Math.Abs(box.SelectionEnd - box.SelectionStart);
        var result = text.Remove(caret, length).Insert(caret, e.Text);
        if (!pattern.IsMatch(result))
            e.Handled = true;
    }
}