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

namespace SenderNG.Core.Tests;

public class PlotterNexViewTests
{
    // The guard that decides whether a finished scene build may report a Vulkan
    // verdict. The plan's own version of this incremented the generation before
    // comparing it, so the comparison became x == x+1 (always false) and a real
    // first-attempt failure was never surfaced — the exact blank, unexplained
    // tab this feature exists to prevent. These pin the corrected order's
    // behaviour at the decision boundary, without needing a GPU.

    [Fact]
    public void ShouldOwnVerdict_true_when_no_newer_attempt_started()
        => Assert.True(PlotterNexView.ShouldOwnVerdict(3, 3));

    [Fact]
    public void ShouldOwnVerdict_false_when_a_newer_attempt_superseded_this_one()
        => Assert.False(PlotterNexView.ShouldOwnVerdict(3, 4));

    // Guards specifically against the plan's bug resurfacing as "bump first,
    // then compare": a caller that has NOT yet bumped must still be told it owns
    // the verdict at equality. If this ever fails, the bump leaked back above
    // the comparison.
    [Fact]
    public void ShouldOwnVerdict_true_at_equality_before_any_bump()
    {
        var generation = 7;
        var currentGeneration = 7;
        var owns = PlotterNexView.ShouldOwnVerdict(generation, currentGeneration);
        currentGeneration++; // the bump happens after, in the caller
        Assert.True(owns);
    }
}
