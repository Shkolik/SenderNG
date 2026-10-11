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
using SenderNG.Services;
using Xunit;

namespace SenderNG.Tests;

public class NumericInputFilterTests
{
    private static TextBox Box(params string[] classes)
    {
        var box = new TextBox();
        foreach (var c in classes)
            box.Classes.Add(c);
        return box;
    }

    private static bool Type(TextBox box, string text)
    {
        var e = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text };
        e.Source = box;
        NumericInputFilter.OnTextInput(box, e);
        return e.Handled;
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("-1.5")]
    [InlineData("123")]
    [InlineData("+1")]
    public void Num_accepts_valid_states(string input) =>
        Assert.False(Type(Box("num"), input));

    [Theory]
    [InlineData("a")]
    [InlineData("1.2.3")]
    [InlineData("--")]
    [InlineData("-1-")]
    public void Num_rejects_invalid_states(string input) =>
        Assert.True(Type(Box("num"), input));

    [Theory]
    [InlineData("")]
    [InlineData("12.5")]
    [InlineData(".5")]
    public void Udec_accepts_valid_states(string input) =>
        Assert.False(Type(Box("udec"), input));

    [Theory]
    [InlineData("-")]
    [InlineData("-5")]
    [InlineData("a")]
    public void Udec_rejects_invalid_states(string input) =>
        Assert.True(Type(Box("udec"), input));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1234567890")]
    public void Int_accepts_valid_states(string input) =>
        Assert.False(Type(Box("int"), input));

    [Theory]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("a")]
    public void Int_rejects_invalid_states(string input) =>
        Assert.True(Type(Box("int"), input));

    [Fact]
    public void Box_without_filter_class_is_untouched() =>
        Assert.False(Type(Box(), "abc"));

    [Fact]
    public void Simulated_insert_replaces_selection()
    {
        var box = Box("num");
        box.Text = "123";
        box.SelectionStart = 1;
        box.SelectionEnd = 2;

        // Selecting the middle "2" and typing "0" gives "103" — valid.
        Assert.False(Type(box, "0"));
        // Selecting the middle "2" and typing "a" would give "1a3" — blocked.
        Assert.True(Type(box, "a"));
    }

    [Fact]
    public void Empty_insert_is_not_swallowed() =>
        Assert.False(Type(Box("num"), ""));
}
