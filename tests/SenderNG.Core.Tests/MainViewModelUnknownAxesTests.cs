/*
 * MainViewModelUnknownAxesTests.cs - a program letter nobody knows is reported at load
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

using CNC.Core;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Fixed;
using SenderNG.ViewModels;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// A program letter that neither the controller's reported letters nor the active
/// profile's CAM aliases resolve means the program is for a different machine: the
/// parser keeps the value at the letter's conventional index and records the letter.
/// That record used to be consumed by nobody, so the mismatch stayed invisible until a
/// wrong axis moved mid-run. The load gate now carries it on
/// <see cref="GCodeProgram.UnknownAxisLetters"/> and the host warns on the status line —
/// the app's load-diagnostic channel (the failure analogue is
/// <c>app.msg.validationFailed</c>).
/// <para>Serialized (<c>LegacyStatics</c>): both tests swap the process-global
/// <see cref="GrblInfo.Current"/> letter set, which every parser construction reads.
/// It runs after the parallel collections, so no parser elsewhere observes the swap.</para>
/// </summary>
[Collection("LegacyStatics")]
public class MainViewModelUnknownAxesTests
{
    /// <summary>Run <paramref name="body"/> with the ambient controller reporting four
    /// letters, so a program's V/U/W words resolve through neither the board nor (no
    /// profile aliases are set here) an alias map.</summary>
    private static void WithFourLetterBoard(Action body)
    {
        var previous = GrblInfo.Current;
        try
        {
            GrblInfo.Current = new GrblInfoState { AxisLetters = "XYZA", NumAxes = 4 };
            body();
        }
        finally
        {
            GrblInfo.Current = previous;
        }
    }

    [Fact]
    public void The_load_gate_carries_the_unknown_letters_off_the_parser() =>
        WithFourLetterBoard(() =>
        {
            var program = GCodeProgram.Parse("G21\nG1 X0 V5.5 F100\n");

            Assert.False(program.HasErrors);
            Assert.Equal(['V'], program.UnknownAxisLetters);
        });

    [Fact]
    public async Task Loading_a_program_with_an_unknown_letter_warns_in_the_status_line()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-unknown-axes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "part.nc");
        await File.WriteAllTextAsync(file, "G21\nG1 X0 V5.5 F100\n");

        var previous = GrblInfo.Current;
        try
        {
            GrblInfo.Current = new GrblInfoState { AxisLetters = "XYZA", NumAxes = 4 };

            var settings = new SettingsService(dir);
            var grbl = new DemoGrblService(settings.Load());
            var services = new ServiceCollection();
            services.AddSingleton(settings);
            services.AddSingleton<IGrblService>(grbl);
            services.AddSingleton<RealGrblService>();
            services.AddSingleton(new ProgramViewModel(grbl));
            var vm = new MainViewModel(grbl, settings, services.BuildServiceProvider());

            vm.SelectRecentCommand.Execute(file); // fire-and-forget async load

            // The "Loaded …" status is the load's last assignment: poll for it, then
            // assert the warning sentence (not the plain loaded one) carries the letter.
            // ("Loading …" precedes it, so match the completed text, not the file name.)
            for (var i = 0; i < 2000 && !vm.Status.Contains("Loaded "); i++)
                await Task.Delay(10);

            Assert.Equal(
                "Loaded part.nc (2 lines), but it uses axis letters this machine does not know: V",
                vm.Status);
        }
        finally
        {
            GrblInfo.Current = previous;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
