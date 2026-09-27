/*
 * Plot3DFailureTests.cs - the engine result-code to user-facing failure-kind
 * mapping, and the exception that carries the code out of the scene constructor
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

using HelixToolkit.Nex;
using SenderNG.Modules.Plotter.Nex.Rendering;

namespace SenderNG.Core.Tests;

public class Plot3DFailureTests
{
    [Theory]
    [InlineData(ResultCode.NotSupported, Plot3DFailure.NotSupported)]
    [InlineData(ResultCode.RuntimeError, Plot3DFailure.RuntimeError)]
    [InlineData(ResultCode.OutOfMemory, Plot3DFailure.OutOfMemory)]
    [InlineData(ResultCode.CompileError, Plot3DFailure.Other)]
    [InlineData(ResultCode.ArgumentError, Plot3DFailure.Other)]
    [InlineData(ResultCode.Unknown, Plot3DFailure.Other)]
    public void Map_translates_each_result_code(ResultCode code, object expected)
        => Assert.Equal(expected, Plot3DFailures.Map(code));

    [Fact]
    public void Map_treats_success_as_other_because_the_caller_never_maps_ok()
        => Assert.Equal(Plot3DFailure.Other, Plot3DFailures.Map(ResultCode.Ok));

    [Fact]
    public void Exception_carries_the_code_it_was_built_with()
        => Assert.Equal(ResultCode.NotSupported, new Plot3DUnavailableException(ResultCode.NotSupported).Code);

    [Fact]
    public void Exception_message_names_the_code_so_the_log_line_is_self_describing()
        => Assert.Contains(
            nameof(ResultCode.NotSupported),
            new Plot3DUnavailableException(ResultCode.NotSupported).Message);
}
