
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

using CNC.Core;
using CNC.Core.Services;
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// G76 threading-cycle expansion (<c>GCodeEmulator.cs</c>) derives its pass count
/// from <c>while (CalculateDOC(++passes) &lt; Depth);</c> where
/// <c>CalculateDOC(p) = J * p^(1/R)</c>. The loop is only bounded by the
/// program itself, so any block whose depth-of-cut curve never crosses K spins
/// forever on the load path (<c>GCodeJob</c> / <c>GCodeLimitsCalculator</c>) and
/// freezes the UI with no recovery.
/// </summary>
public class G76ThreadingTests
{
    /// <summary>
    /// <c>J</c> is the initial depth of cut. J0 with K&gt;0 is the reported shape:
    /// the parser requires J to be present and non-negative but never positive,
    /// so the block parses clean and <c>J * p^(1/R)</c> is then 0 for every pass,
    /// leaving <c>0 &lt; K</c> true forever.
    /// </summary>
    [Theory]
    [InlineData("G18 G76 P1 I1 J0 K1 R1 Z-10")]
    [InlineData("G18 G76 P1 I1 J0 K1 Z-10")]
    public void Zero_initial_depth_is_reported_as_a_parse_error(string block)
    {
        var program = GCodeProgram.Parse(block + "\n");

        Assert.True(program.HasErrors, $"'{block}' parsed clean, so the emulator loops forever on it");
        Assert.DoesNotContain(program.Tokens, t => t is GCThreadingMotion);
    }

    /// <summary>
    /// The same unbounded condition is reachable with a legal, non-zero J: the
    /// parser only requires R &ge; 1, and a very large R makes the depth curve
    /// approach J asymptotically, so it never crosses K either.
    /// </summary>
    [Fact]
    public async Task Extreme_depth_degression_does_not_spin_the_pass_counter()
    {
        var program = GCodeProgram.Parse("G18 G76 P1 I1 J1 K2 R1000000000 Z-10\n");
        Assert.False(program.HasErrors);

        var tokens = program.Tokens.ToList();
        var expansion = Task.Run(() => new GCodeEmulator(true).Execute(tokens).Count());

        var finished = await Task.WhenAny(expansion, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.True(ReferenceEquals(finished, expansion),
            "G76 expansion never returned: the pass-count loop has no upper bound");

        Assert.InRange(await expansion, 1, 200_000);
    }

    /// <summary>
    /// Guards the fix against changing legitimate output: these two shapes are
    /// unaffected by any pathological input and must keep producing exactly the
    /// actions they produced before (9 and 110).
    /// </summary>
    [Theory]
    [InlineData("G18 G76 P1 I1 J0.5 K1 R1 Z-10", 9)]
    [InlineData("G18 G76 P1 I1 J0.5 K1 Z-10", 9)]
    [InlineData("G18 G76 P1 I1 J0.2 K1 R2 Q30 H2 Z-10", 110)]
    public void Valid_threading_cycle_expands_to_the_same_actions(string block, int expectedActions)
    {
        var program = GCodeProgram.Parse(block + "\n");
        Assert.False(program.HasErrors);

        var actions = new GCodeEmulator(true).Execute(program.Tokens.ToList()).ToList();

        Assert.Equal(expectedActions, actions.Count);
    }
}
