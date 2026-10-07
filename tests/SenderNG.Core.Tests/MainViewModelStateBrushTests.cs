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

using Avalonia;
using Avalonia.Media;
using SenderNG.ViewModels;
using Xunit;

namespace SenderNG.Core.Tests;

public class MainViewModelStateBrushTests
{
    // One [Fact] over a case table rather than a [Theory]: every InlineData row
    // counts as its own test case in xunit 2.x, which would change the gate
    // total this plan's arithmetic depends on.
    [Fact]
    public void ResolveStateBrush_maps_every_documented_status()
    {
        // The four colours, verbatim from MainViewModel.cs:1592-1595.
        Assert.Equal((0xc8, 0xe8, 0xc8), Rgb("Run"));
        Assert.Equal((0xe0, 0x45, 0x45), Rgb("Alarm"));
        Assert.Equal((0xff, 0xe8, 0xa0), Rgb("Idle"));
        Assert.Equal((0xff, 0xe8, 0xa0), Rgb("Hold:0"));
        Assert.Equal((0xff, 0xe8, 0xa0), Rgb("Check"));
        Assert.Equal((0xe0, 0xe0, 0xe0), Rgb("Door"));

        // OrdinalIgnoreCase, as before.
        Assert.Equal((0xc8, 0xe8, 0xc8), Rgb("RUN"));
    }

    [Fact]
    public void ResolveStateBrush_returns_the_same_cached_brush()
    {
        Assert.Same(
            MainViewModel.ResolveStateBrush("Run"),
            MainViewModel.ResolveStateBrush("Run"));

        // idle and hold share one brush; Same proves it is literally the same object.
        Assert.Same(
            MainViewModel.ResolveStateBrush("Idle"),
            MainViewModel.ResolveStateBrush("Hold:0"));
    }

    private static (byte R, byte G, byte B) Rgb(string status) =>
        ToRgb(MainViewModel.ResolveStateBrush(status));

    private static (byte R, byte G, byte B) ToRgb(IBrush brush)
    {
        var sc = Assert.IsType<SolidColorBrush>(brush);
        // MainViewModel caches these brushes in static readonly fields, so each is
        // constructed exactly once - on whichever thread reaches them first. The typed
        // Color getter calls AvaloniaObject.VerifyAccess() against the dispatcher
        // captured at that construction, and a test class running in parallel on a
        // different xunit worker thread is not it. Widening the property to
        // AvaloniaProperty selects the overload that performs no affinity check; the
        // value is immutable once constructed, so reading it from here is safe.
        AvaloniaProperty colorProperty = SolidColorBrush.ColorProperty;
        var color = (Color)sc.GetValue(colorProperty)!;
        return (color.R, color.G, color.B);
    }
}
