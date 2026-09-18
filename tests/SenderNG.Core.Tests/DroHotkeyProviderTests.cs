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

using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class DroHotkeyProviderTests
{
    private static (IReadOnlyList<IHotkeyAction> Actions, DemoGrblService Grbl) Make()
    {
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-drohotkey-" + Guid.NewGuid().ToString("N")));
        var demo = new DemoGrblService(settings.Load());
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(demo);
        services.AddSingleton<IGrblService>(demo);
        services.AddSingleton<DroViewModel>();
        var provider = services.BuildServiceProvider();
        return (DroHotkeyProvider.Create(provider), demo);
    }

    [Fact]
    public void Every_letter_and_all_are_declared()
    {
        var (actions, _) = Make();
        var ids = actions.Select(a => a.ActionId).ToList();
        foreach (var axis in new[] { "x", "y", "z", "a", "b", "c" })
            Assert.Contains($"dro.zero.{axis}", ids);
        Assert.Contains("dro.zero.all", ids);
    }

    [Fact]
    public async Task Axis_zero_is_gated_on_connection_and_axis_count()
    {
        var (actions, demo) = Make();
        Assert.False(actions.Single(a => a.ActionId == "dro.zero.x").CanExecute());

        await demo.ConnectAsync("demo");
        Assert.True(actions.Single(a => a.ActionId == "dro.zero.x").CanExecute());
        // Demo reports 3 axes by default: X/Y/Z live, A/B/C inert.
        Assert.False(actions.Single(a => a.ActionId == "dro.zero.a").CanExecute());
    }
}