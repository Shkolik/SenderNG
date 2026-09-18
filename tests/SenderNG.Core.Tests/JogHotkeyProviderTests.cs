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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class JogHotkeyProviderTests
{
    private sealed class RecordingGrbl(int numAxes = 3) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(numAxes));
        public bool IsConnected { get; set; }
        public ControllerCapabilities Capabilities => ControllerCapabilities.Unknown;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) { IsConnected = true; return Task.CompletedTask; }
        public void Disconnect() => IsConnected = false;
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void Push(MachineState state) => _subject.OnNext(state);
    }

    private static (IReadOnlyList<IHotkeyAction> Actions, JogViewModel Jog, RecordingGrbl Grbl)
        Make(string mode = "Mill", int numAxes = 3)
    {
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-joghotkey-" + Guid.NewGuid().ToString("N")));
        var app = settings.Load();
        app.MachineTypeId = mode;
        settings.Save(app);
        var grbl = new RecordingGrbl(numAxes);
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<IGrblService>(_ => grbl);
        services.AddSingleton<JogViewModel>();
        var provider = services.BuildServiceProvider();
        var jog = provider.GetRequiredService<JogViewModel>();
        return (JogHotkeyProvider.Create(provider), jog, grbl);
    }

    [Fact]
    public void Action_ids_are_complete_and_unique()
    {
        var (actions, _, _) = Make();
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
        var (actions, jog, _) = Make();
        actions.Single(a => a.ActionId == "jog.step.2").OnKeyDown();
        Assert.Equal(2, jog.SelectedDistanceIndex);
    }

    [Fact]
    public void Feed_selection_changes_the_jog_speed_index()
    {
        var (actions, jog, _) = Make();
        actions.Single(a => a.ActionId == "jog.feed.0").OnKeyDown();
        Assert.Equal(0, jog.SelectedSpeedIndex);
    }

    [Fact]
    public void Step_increment_clamps_at_the_last_slot()
    {
        var (actions, jog, _) = Make();
        jog.SelectedDistanceIndex = 3;
        actions.Single(a => a.ActionId == "jog.step.inc").OnKeyDown();
        Assert.Equal(3, jog.SelectedDistanceIndex);
    }

    [Fact]
    public void Direction_actions_are_repeat_on_hold()
    {
        var (actions, _, _) = Make();
        Assert.True(actions.Single(a => a.ActionId == "jog.left").RepeatOnHold);
        Assert.False(actions.Single(a => a.ActionId == "jog.step.1").RepeatOnHold);
    }

    [Fact]
    public void Direction_actions_are_gated_on_connection()
    {
        var (actions, _, _) = Make();
        Assert.False(actions.Single(a => a.ActionId == "jog.left").CanExecute());
    }

    [Fact]
    public void Together_actions_are_registered_with_ids_and_repeat_on_hold()
    {
        var (actions, _, _) = Make();
        Assert.Contains(actions, a => a.ActionId == "jog.together.left" && a.RepeatOnHold);
        Assert.Contains(actions, a => a.ActionId == "jog.together.right" && a.RepeatOnHold);
        Assert.Contains(actions, a => a.ActionId == "jog.together.up" && a.RepeatOnHold);
        Assert.Contains(actions, a => a.ActionId == "jog.together.down" && a.RepeatOnHold);
        Assert.Equal(actions.Select(a => a.ActionId).Count(),
            actions.Select(a => a.ActionId).Distinct().Count());
    }

    [Fact]
    public void Together_actions_are_inert_on_a_mill()
    {
        var (actions, _, grbl) = Make(); // Mill, 3 axes
        grbl.ConnectAsync("demo");
        Assert.False(actions.Single(a => a.ActionId == "jog.together.left").CanExecute());
    }

    [Fact]
    public void Together_actions_can_execute_on_foam()
    {
        var savedLetters = CNC.Core.GrblInfo.AxisLetters;
        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        try
        {
            CNC.Core.GrblInfo.AxisLetters = "XYZA";
            CNC.Core.GrblInfo.NumAxes = 4;
            var (actions, _, grbl) = Make("Foam", 4);
            grbl.ConnectAsync("demo");
            Assert.True(actions.Single(a => a.ActionId == "jog.together.left").CanExecute());
            Assert.True(actions.Single(a => a.ActionId == "jog.together.down").CanExecute());
        }
        finally
        {
            CNC.Core.GrblInfo.AxisLetters = savedLetters;
            CNC.Core.GrblInfo.NumAxes = savedAxes;
        }
    }

    [Fact]
    public void Together_actions_default_to_alt_arrows()
    {
        var (actions, _, _) = Make("Foam", 4);
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-joghotkey-" + Guid.NewGuid().ToString("N")));
        var service = new HotkeyService(settings);
        var defaultsByActionId = new JogModule().HotKeys.ToDictionary(h => h.ActionId, h => h.Gesture);
        foreach (var action in actions)
            service.RegisterAction(action, defaultsByActionId.GetValueOrDefault(action.ActionId, ""));

        Assert.Equal("Alt+Left", service.DefaultGestureFor("jog.together.left"));
        Assert.Equal("Alt+Right", service.DefaultGestureFor("jog.together.right"));
        Assert.Equal("Alt+Up", service.DefaultGestureFor("jog.together.up"));
        Assert.Equal("Alt+Down", service.DefaultGestureFor("jog.together.down"));
    }
}
