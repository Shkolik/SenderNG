/*
 * PlotterNexViewTests.cs - the pure, GPU-free decision logic extracted from
 * PlotterNexView, which is otherwise not unit-testable (it needs an Avalonia
 * dispatcher and a real GPU).
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

using SenderNG.Modules.Plotter.Nex;
using SenderNG.Modules.Plotter.Nex.Rendering;

namespace SenderNG.Core.Tests;

public class PlotterNexViewTests
{
    // The generation guard, including the ORDER of its two halves. The plan's
    // version advanced the generation before comparing, so the comparison became
    // x == x+1, always false, and a real first-attempt failure was never
    // reported - the blank unexplained tab this feature exists to prevent.
    //
    // These assert the generation value AFTER the call on purpose. An earlier
    // version of this file tested a pure `generation == current` helper and so
    // passed happily while the call site still had the broken sequence: the bug
    // was in the order, not the comparison, so the order is what is under test.

    [Fact]
    public void ShouldReportFailure_reports_at_equality_and_advances_the_generation()
    {
        var current = 3;
        Assert.True(PlotterNexView.ShouldReportFailure(3, ref current));
        Assert.Equal(4, current);
    }

    // The regression test for the plan's bug, stated as directly as it can be:
    // at equality the attempt MUST own the verdict. If the advance ever moves
    // back above the comparison, this fails.
    [Fact]
    public void ShouldReportFailure_owns_the_verdict_at_equality()
    {
        var current = 7;
        Assert.True(PlotterNexView.ShouldReportFailure(7, ref current));
    }

    [Fact]
    public void ShouldReportFailure_stays_silent_when_a_newer_attempt_superseded_this_one()
    {
        var current = 4;
        Assert.False(PlotterNexView.ShouldReportFailure(3, ref current));
        // And it must NOT advance: the superseded attempt is not allowed to
        // invalidate the newer one that replaced it.
        Assert.Equal(4, current);
    }

    // A superseded failure is remembered but not reported, which is the whole
    // point of splitting the cache from the report: forgetting it re-enters
    // Vulkan on the next tab return and leaks another VkInstance per attach.
    [Fact]
    public void RememberFailure_keeps_the_first_reason_so_the_remember_merge_is_lossless()
    {
        var cached = Plot3DFailure.None;

        var afterFirst = PlotterNexView.RememberFailure(cached, Plot3DFailure.NotSupported);
        Assert.Equal(Plot3DFailure.NotSupported, afterFirst);

        // A later, different failure must not overwrite what the user may
        // already have been shown.
        var afterSecond = PlotterNexView.RememberFailure(afterFirst, Plot3DFailure.Other);
        Assert.Equal(Plot3DFailure.NotSupported, afterSecond);
    }

    [Fact]
    public void RememberFailure_is_a_no_op_once_a_verdict_is_cached()
    {
        Assert.Equal(
            Plot3DFailure.RuntimeError,
            PlotterNexView.RememberFailure(Plot3DFailure.RuntimeError, Plot3DFailure.NotSupported));
    }
}
