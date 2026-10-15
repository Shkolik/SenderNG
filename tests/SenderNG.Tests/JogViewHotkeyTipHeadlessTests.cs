
/*
 * JogViewHotkeyTipHeadlessTests.cs - the plane-2 cross tooltips carry the letter
 * the key actually jogs on the current machine.
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

using System.Reactive.Subjects;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Tests;

/// <summary>
/// The plane-2 cross buttons' tooltips bind the live label properties, not a static
/// catalog string: on plane-2 slots <c>[3, 2]</c> the horizontal button must show the
/// letter <c>A</c> even though the action ID still says <c>z</c>. The letter itself is
/// asserted in the Core tests; this proves the view is wired to the live properties.
/// </summary>
public sealed class JogViewHotkeyTipHeadlessTests
{
    private sealed class GrblFourAxes : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _state = new(new MachineState(4));
        public bool IsConnected { get; set; } = true;
        public ControllerCapabilities Capabilities => ControllerCapabilities.Unknown;
        public IObservable<MachineState> State => _state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Plane2_cross_tooltips_bind_the_live_label_properties()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-jogtip-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Foam;
        var planes = MachineTypeCatalog.Resolve(MachineTypes.Foam).CreateDefaultPlanes();
        planes[1].Axes[0].Index = 3;   // plane 2 horizontal = A
        planes[1].Axes[1].Index = 2;   // plane 2 vertical   = Z
        profile.Planes = planes;
        settings.Save(profile);

        var vm = new JogViewModel(new GrblFourAxes(), settings,
            new CNC.Core.GrblInfoState { AxisLetters = "XYZA", NumAxes = 4 });
        var view = new JogView { DataContext = vm };
        var window = new Window { Width = 900, Height = 640, Content = view };
        window.Show();
        await Pump();
        await Pump();

        // The plane-2 tags are the cross's own command parameters on this layout.
        var byTag = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.CommandParameter is string)
            .GroupBy(b => (string)b.CommandParameter!)
            .ToDictionary(g => g.Key, g => g.First());

        Assert.Equal(vm.HotkeyTipPlane2Right, ToolTip.GetTip(byTag["A+"]) as string);
        Assert.Equal(vm.HotkeyTipPlane2Left, ToolTip.GetTip(byTag["A-"]) as string);
        Assert.Equal(vm.HotkeyTipPlane2Up, ToolTip.GetTip(byTag["Z+"]) as string);
        Assert.Equal(vm.HotkeyTipPlane2Down, ToolTip.GetTip(byTag["Z-"]) as string);
        // A raw format string would mean the locale value was shown unformatted.
        Assert.DoesNotContain("{0}", vm.HotkeyTipPlane2Right);

        window.Close();
    }

    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}
