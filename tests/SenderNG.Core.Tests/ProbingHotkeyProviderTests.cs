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
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class ProbingHotkeyProviderTests
{
    private static IReadOnlyList<IHotkeyAction> Make()
    {
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-probehotkey-" + Guid.NewGuid().ToString("N")));
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<IGrblService>(sp => sp.GetRequiredService<DemoGrblService>());
        services.AddSingleton<ProbeViewModel>();
        return ProbingHotkeyProvider.Create(services.BuildServiceProvider());
    }

    [Fact]
    public void Probe_actions_are_module_scoped_to_probing()
    {
        var actions = Make();
        foreach (var action in actions)
        {
            Assert.Equal(HotkeyScope.Module, action.Scope);
            Assert.Equal("probing", action.ModuleId);
        }
    }

    [Fact]
    public void Connected_action_is_gated_off()
    {
        var actions = Make();
        Assert.False(actions.Single(a => a.ActionId == "probe.connected").CanExecute());
    }

    [Fact]
    public void Start_and_stop_wrap_the_view_model_commands()
    {
        var actions = Make();
        Assert.Contains(actions, a => a.ActionId == "probe.start");
        Assert.Contains(actions, a => a.ActionId == "probe.stop");
    }
}