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

using System.Globalization;
using CNC.Core;
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

// GCFlowControl.ToString() writes the O-word statement keyword into generated
// G-code with a culture-sensitive ToLower(). Under tr-TR that maps I to the
// dotless "ı", so ENDIF serialises as "endıf" and IF as "ıf" - output that
// cannot be re-parsed. Same defect in the read direction at
// NGCExpr.ReadFlowControl's ToUpper() (audit §1.16).
//
// The token is built directly rather than through the parser because
// GCodeParser.ExpressionsSupported defaults to false and nothing in the repo
// enables it, so ParseBlock cannot produce a flow-control token on its own.

public class FlowControlSerializationTests
{
    private static GCFlowControl Token(NGCExpr.FlowControl fc, string expression = "[1]") =>
        new(CNC.GCode.Commands.FlowControl, 1, 0, fc, expression);

    public static TheoryData<NGCExpr.FlowControl, string> Statements => new()
    {
        { NGCExpr.FlowControl.IF, "if" },
        { NGCExpr.FlowControl.ELSEIF, "elseif" },
        { NGCExpr.FlowControl.ENDIF, "endif" },
        { NGCExpr.FlowControl.WHILE, "while" },
        { NGCExpr.FlowControl.ENDWHILE, "endwhile" },
        { NGCExpr.FlowControl.CONTINUE, "continue" },
        { NGCExpr.FlowControl.IF, "if" },
    };

    [Theory]
    [MemberData(nameof(Statements))]
    public void Serialised_statement_is_ascii_under_a_turkish_culture(
        NGCExpr.FlowControl fc, string expectedKeyword)
    {
        // Every keyword containing an uppercase I in the enum name is the hazard.
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            var text = Token(fc).ToString();

            Assert.Contains(expectedKeyword, text);
            Assert.All(text, c => Assert.True(c < 128, $"'{c}' (U+{(int)c:X4}) is not ASCII in '{text}'"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Serialised_statement_does_not_depend_on_the_ambient_culture()
    {
        // Same token, two cultures: the generated G-code must be byte-identical,
        // or a transform run on a Turkish-locale machine emits different text
        // than the same program on any other machine.
        var saved = CultureInfo.CurrentCulture;
        string underInvariant, underTurkish;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            underInvariant = Token(NGCExpr.FlowControl.ENDIF).ToString();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            underTurkish = Token(NGCExpr.FlowControl.ENDIF).ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        Assert.Equal(underInvariant, underTurkish);
    }

    [Fact]
    public void Serialised_keyword_reparses_to_the_same_statement()
    {
        // The point of the fix: what ToString() writes must read back.
        foreach (var fc in Enum.GetValues<NGCExpr.FlowControl>())
        {
            if (fc == NGCExpr.FlowControl.NoOp)
                continue;

            var text = Token(fc).ToString();
            var machine = new Machine();
            var expr = new NGCExpr(machine);

            int pos = text.IndexOf('O') + 2;   // past "O" and the O-word value
            var status = expr.ReadFlowControl(text, ref pos, out var back);

            Assert.Equal(NGCExpr.OpStatus.OK, status);
            Assert.Equal(fc, back);
        }
    }
}
