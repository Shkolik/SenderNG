/*
 * JogViewStatusLayoutHeadlessTests.cs - the jog status line must not resize the pad row
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

public sealed class JogViewStatusLayoutHeadlessTests
{
    private sealed class FaultyGrbl : IGrblService
    {
        public Exception? SendFault { get; set; }
        public bool IsConnected { get; set; } = true;
        public ControllerCapabilities Capabilities { get; } =
            ControllerCapabilities.Unknown with { MaxTravel = [200, 300, 100, 50] };
        private readonly BehaviorSubject<MachineState> _state = new(new MachineState(4));
        public IObservable<MachineState> State => _state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }

        public Task SendAsync(string line)
        {
            // Throwing synchronously fills Status before Execute() returns, so no await is needed.
            if (SendFault is not null)
                throw SendFault;
            return Task.CompletedTask;
        }
    }

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-headless-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var app = settings.Load();
        app.MachineTypeId = "Mill";
        settings.Save(app);
        return settings;
    }

    [AvaloniaFact]
    public async Task Status_text_never_resizes_the_pad_row()
    {
        // Regression: the status TextBlock used to be the last child of the
        // horizontally-measured row inside a ScrollViewer. A long message widened
        // that row, the layout re-flowed, and the button slid out from under the
        // held pointer - stopping a continuous jog the instant status appeared.
        var grbl = new FaultyGrbl();
        var vm = new JogViewModel(grbl, MakeSettings());

        var view = new JogView { DataContext = vm };
        var window = new Window { Width = 640, Height = 640, Content = view };
        window.Show();
        await Pump();
        await Pump();

        var row = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "JogRow");
        var slot = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "StatusSlot");

        var idleRowWidth = row.DesiredSize.Width;
        var idleSlotHeight = slot.Bounds.Height;
        var idleSlotWidth = slot.Bounds.Width;
        Assert.Equal(32, idleSlotHeight, 1);              // the slot is reserved, never collapsed

        grbl.SendFault = new InvalidOperationException(new string('x', 400));
        vm.JogCommand.Execute("X+");
        await Pump();
        await Pump();

        var status = vm.Status ?? string.Empty;
        Assert.True(status.Length > 100, $"status not populated: '{status}'");
        Assert.Equal(idleRowWidth, row.DesiredSize.Width);    // row width is independent of status
        Assert.Equal(idleSlotHeight, slot.Bounds.Height);      // slot never collapses or grows
        Assert.Equal(idleSlotWidth, slot.Bounds.Width);        // bounded: cannot drive drawer width

        var rowText = string.Join(" ", row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.DoesNotContain("failed", rowText);              // status does not live in the row

        window.Close();
    }

    /// <summary>Yield so the headless SynchronizationContext runs a pending layout/render.</summary>
    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}
