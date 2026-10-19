/*
 * GlContextMacSmokeTests.cs - env-gated end-to-end proof that the macOS CGL
 * backend creates a usable core-profile context
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

using SenderNG.Modules.Common.Render;
using Xunit.Abstractions;

namespace SenderNG.Core.Tests;

/// <summary>The on-demand check that a Mac really backs the CGL backend
/// end-to-end: enabled only by SENDERNG_GL_SMOKE=1 on a macOS host (see
/// <see cref="GlSmokeFactAttribute"/>). The report-only gl-smoke-macos CI
/// job that set it on GitHub's runners was retired after it confirmed the
/// CGL path there; run it by hand on a Mac to re-check. Context creation
/// failure here is a failure, not a skip, when it is requested.</summary>
[Trait("Category", "GLSmoke")]
public class GlContextMacSmokeTests
{
    private readonly ITestOutputHelper _output;

    public GlContextMacSmokeTests(ITestOutputHelper output) => _output = output;

    [GlSmokeFact]
    public void Create_reports_a_usable_core_context()
    {
        using var ctx = GlContext.Create();
        _output.WriteLine(
            $"version={ctx.Version} | renderer=\"{ctx.Renderer}\" | vendor=\"{ctx.Vendor}\" | " +
            $"profileMask=0x{ctx.ProfileMask:X} | flags=0x{ctx.ContextFlagsMask:X} | {ctx.CreationPath}");
        Assert.False(ctx.IsGles);
        Assert.StartsWith("path=cgl-", ctx.CreationPath);
        Assert.Equal(0x1, ctx.ProfileMask);  // GL_CONTEXT_CORE_PROFILE_BIT
    }
}
