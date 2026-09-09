
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

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using Xunit;

/// <summary>
/// Regression: RegisterModule used to register every built-in module twice under
/// IModule (generic inference from the IModule-typed parameter), doubling sidebar entries.
/// </summary>
[Collection("LocalizationState")] // BuildHost sets LocalizationService.Current
public class HostModuleRegistrationTests
{
    [Fact]
    public void BuiltIn_modules_are_registered_exactly_once()
    {
        var host = SenderNG.HostSetup.BuildHost([]);
        var modules = host.Services.GetServices<IModule>().ToList();
        var ids = modules.Select(m => m.Id).OrderBy(i => i).ToList();
        Assert.True(modules.Count == modules.DistinctBy(m => m.Id).Count(),
            $"duplicates ({modules.Count}): {string.Join(",", ids)}");
        // 15 built-in + 5 staged plugins (Dummy + Excellon + HPGL + Plotter +
        // Plotter.Nex) discovered via the Modules/ directory scan — the real
        // drop-in path.
        Assert.Equal(20, modules.Count);
    }
}
