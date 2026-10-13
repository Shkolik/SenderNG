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

using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// A scanned plugin must be registered as the SAME instance under its concrete
/// type, <see cref="IModule"/>, and every capability interface it registered in
/// <c>RegisterServices</c> (which captures <c>this</c>). If the catalog registers
/// the type instead, the container builds a second instance: <c>AttachServices</c>
/// (host) runs on one while the capability interface resolves the other, so state
/// set at attach time is invisible to the interface — the height-map viewer's
/// <c>ShowAsync</c> silently no-ops because its <c>_services</c> is null.
/// </summary>
[Collection("LocalizationState")] // BuildHost sets LocalizationService.Current
public class PluginInstanceIdentityTests
{
    [Fact]
    public void Scanned_plugin_is_one_instance_for_IModule_and_its_capability_interfaces()
    {
        var host = SenderNG.HostSetup.BuildHost([]);
        var modules = host.Services.GetServices<IModule>().ToList();

        var viewer = modules.Single(m => m.Id == "heightmap-viewer");
        var renderer = host.Services.GetRequiredService<IHeightMap3DRenderer>();
        Assert.Same(viewer, renderer);

        var probing = modules.Single(m => m.Id == "probing");
        var source = host.Services.GetRequiredService<IHeightMapSource>();
        Assert.Same(probing, source);
    }
}
