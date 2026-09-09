/*
 * PlotterProfileSwitchTests.cs - Profile-switch regression repro
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

using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;
using SenderNG.Modules.Plotter;
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class PlotterProfileSwitchTests
{
    private static (SettingsService Settings, string MillProfileId) MakeSettings(string foamProfileId)
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = new SettingsService(dir);
        var foam = settings.Load();
        foam.MachineTypeId = MachineTypes.Foam;
        foam.DemoAxes = 4;
        settings.Save(foam);
        var mill = settings.CreateProfile("mill", MachineTypes.Mill);
        settings.Save(mill);
        return (settings, mill.Id);
    }

    /// <summary>User-reported sequence: foam profile → load → run → stop → close
    /// file → disconnect → switch profile to mill → connect → load program.
    /// The plot showed "No program loaded." while the header counted correctly.</summary>
    [Fact]
    public void Plotter_recovers_after_profile_switch_and_reload()
    {
        var (settings, millId) = MakeSettings(MachineTypes.Foam);
        var demo = new DemoGrblService(settings.Load(), pollIntervalMs: 10);
        var programVm = new ProgramViewModel(demo);
        var plotter = new PlotterViewModel(programVm, demo, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())));

        // Foam profile: load a 4-axis program.
        programVm.LoadGCode("G21\nG90\nG1 X10 Y2 Z8 A2 F100\nG1 X20 Y4 Z16 A4");
        Assert.True(plotter.HasGeometry);
        Assert.Null(plotter.StatusText);

        // Run (demo), then close the file.
        programVm.Rewind();
        programVm.ClearProgram();
        Assert.False(plotter.HasGeometry);

        // Disconnect + profile switch (the app clears the program again).
        demo.Disconnect();
        settings.Activate(millId);
        programVm.ClearProgram();

        // Connect to the mill demo and load a 3-axis program.
        demo.SetNumAxes(3);
        _ = demo.ConnectAsync("demo");
        programVm.LoadGCode("G21\nG90\nG1 X5 Y5 F100\nG1 X15 Y5\nG1 X15 Y15\nG1 X5 Y15");

        Assert.True(plotter.HasGeometry);
        Assert.Null(plotter.StatusText);
        Assert.Equal("XYZ", plotter.PlaneLetters[0]);
    }

    /// <summary>The same sequence with a program big enough to take the async
    /// build path — the generation guard must not leave the plotter empty.</summary>
    [Fact]
    public async Task Plotter_recovers_after_profile_switch_with_async_build()
    {
        var (settings, millId) = MakeSettings(MachineTypes.Foam);
        var demo = new DemoGrblService(settings.Load(), pollIntervalMs: 10);
        var programVm = new ProgramViewModel(demo);
        var plotter = new PlotterViewModel(programVm, demo, settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())));

        // Big foam program (async build).
        var big = new System.Text.StringBuilder();
        for (var i = 0; i < 3000; i++)
            big.AppendLine($"G1 X{i % 100} Y{i % 50} Z{i % 80} A{i % 20} F100");
        programVm.LoadGCode(big.ToString());
        await WaitFor(() => plotter.HasGeometry);

        programVm.Rewind();
        programVm.ClearProgram();

        demo.Disconnect();
        settings.Activate(millId);
        programVm.ClearProgram();

        demo.SetNumAxes(3);
        _ = demo.ConnectAsync("demo");
        var mill = new System.Text.StringBuilder();
        for (var i = 0; i < 3000; i++)
            mill.AppendLine($"G1 X{i % 100} Y{i % 50} F100");
        programVm.LoadGCode(mill.ToString());

        await WaitFor(() => plotter.HasGeometry);
        Assert.Null(plotter.StatusText);
        Assert.Equal("XYZ", plotter.PlaneLetters[0]);
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 15000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }
}