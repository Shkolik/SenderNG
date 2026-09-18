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

public class JogHotkeyProviderTests
{
    private static (IReadOnlyList<IHotkeyAction> Actions, JogViewModel Jog) Make()
    {
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-joghotkey-" + Guid.NewGuid().ToString("N")));
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<IGrblService>(sp => sp.GetRequiredService<DemoGrblService>());
        services.AddSingleton<JogViewModel>();
        var provider = services.BuildServiceProvider();
        var jog = provider.GetRequiredService<JogViewModel>();
        return (JogHotkeyProvider.Create(provider), jog);
    }

    [Fact]
    public void Action_ids_are_complete_and_unique()
    {
        var (actions, _) = Make();
        var ids = actions.Select(a => a.ActionId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("jog.step.0", ids);
        Assert.Contains("jog.step.3", ids);
        Assert.Contains("jog.feed.2", ids);
        Assert.Contains("jog.step.inc", ids);
        Assert.Contains("jog.feed.dec", ids);
        Assert.Contains("jog.left", ids);
        Assert.Contains("jog.right", ids);
        Assert.Contains("jog.up", ids);
        Assert.Contains("jog.down", ids);
        Assert.Contains("jog.z.plus", ids);
        Assert.Contains("jog.z.minus", ids);
        Assert.Contains("jog.a.plus", ids);
        Assert.Contains("jog.a.minus", ids);
    }

    [Fact]
    public void Step_selection_changes_the_jog_distance_index()
    {
        var (actions, jog) = Make();
        actions.Single(a => a.ActionId == "jog.step.2").OnKeyDown();
        Assert.Equal(2, jog.SelectedDistanceIndex);
    }

    [Fact]
    public void Feed_selection_changes_the_jog_speed_index()
    {
        var (actions, jog) = Make();
        actions.Single(a => a.ActionId == "jog.feed.0").OnKeyDown();
        Assert.Equal(0, jog.SelectedSpeedIndex);
    }

    [Fact]
    public void Step_increment_clamps_at_the_last_slot()
    {
        var (actions, jog) = Make();
        jog.SelectedDistanceIndex = 3;
        actions.Single(a => a.ActionId == "jog.step.inc").OnKeyDown();
        Assert.Equal(3, jog.SelectedDistanceIndex);
    }

    [Fact]
    public void Direction_actions_are_repeat_on_hold()
    {
        var (actions, _) = Make();
        Assert.True(actions.Single(a => a.ActionId == "jog.left").RepeatOnHold);
        Assert.False(actions.Single(a => a.ActionId == "jog.step.1").RepeatOnHold);
    }

    [Fact]
    public void Direction_actions_are_gated_on_connection()
    {
        var (actions, _) = Make();
        Assert.False(actions.Single(a => a.ActionId == "jog.left").CanExecute());
    }
}