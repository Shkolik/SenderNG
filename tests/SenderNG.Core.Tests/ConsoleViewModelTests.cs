
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
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class ConsoleViewModelTests
{
    private sealed class LineGrbl : IGrblService
    {
        public Subject<string> Lines { get; } = new();
        public bool IsConnected => true;
        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Empty<MachineState>();
        public IObservable<string> ReceivedLines => Lines;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    // A service that does not publish raw lines (like DemoGrblService) must
    // leave the console silent — no synthesized/mocked status flood.
    private sealed class SilentGrbl : IGrblService
    {
        public bool IsConnected => true;
        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Empty<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-console-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    [Fact]
    public void Meaningful_lines_show_without_verbose()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());

        grbl.Lines.OnNext("Grbl 1.1h ['$' for help]");
        grbl.Lines.OnNext("[OPT:V,15,128]");
        grbl.Lines.OnNext("error:23");

        Assert.Contains("Grbl 1.1h ['$' for help]", vm.ConsoleLines);
        Assert.Contains("[OPT:V,15,128]", vm.ConsoleLines);
        // Error replies get their dictionary explanation appended (ioSender format).
        Assert.Contains("error:23 - G-code command in block requires an integer value.", vm.ConsoleLines);
    }

    [Fact]
    public void Unknown_error_reply_stays_raw()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());

        grbl.Lines.OnNext("error:200");

        Assert.Contains("error:200", vm.ConsoleLines);
    }

    [Fact]
    public void Noisy_lines_hidden_without_verbose()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());

        grbl.Lines.OnNext("<Idle|WPos:0,0,0|FS:0,0>");
        grbl.Lines.OnNext("ok");
        grbl.Lines.OnNext("$110=800.00 (x mm/min)");

        Assert.Empty(vm.ConsoleLines);
    }

    [Fact]
    public void Verbose_shows_status_and_ok()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());
        vm.Verbose = true;

        grbl.Lines.OnNext("<Idle|WPos:0,0,0>"); // first status: changed -> shown
        grbl.Lines.OnNext("<Idle|WPos:0,0,0>"); // identical status: hidden (ShowRTAll off)
        grbl.Lines.OnNext("ok");

        Assert.Contains("<Idle|WPos:0,0,0>", vm.ConsoleLines);
        Assert.Contains("ok", vm.ConsoleLines);
        Assert.Single(vm.ConsoleLines, l => l.StartsWith("<Idle"));
    }

    [Fact]
    public void ShowAllRealtime_keeps_unchanged_status_reports()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());
        vm.Verbose = true;
        vm.ShowAllRealtime = true;

        grbl.Lines.OnNext("<Idle|WPos:0,0,0>");
        grbl.Lines.OnNext("<Idle|WPos:0,0,0>");

        Assert.Equal(2, vm.ConsoleLines.Count(l => l.StartsWith("<Idle")));
    }

    [Fact]
    public void FilterRealtime_hides_status_even_in_verbose()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());
        vm.Verbose = true;
        vm.FilterRealtime = true;

        grbl.Lines.OnNext("<Idle|WPos:0,0,0>");
        grbl.Lines.OnNext("ok");

        Assert.DoesNotContain(vm.ConsoleLines, l => l.StartsWith("<"));
        Assert.Contains("ok", vm.ConsoleLines);
    }

    [Fact]
    public void FilterOk_hides_ok_replies()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());
        vm.Verbose = true;
        vm.FilterOk = true;

        grbl.Lines.OnNext("ok");

        Assert.DoesNotContain(vm.ConsoleLines, l => l == "ok");
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());
        grbl.Lines.OnNext("Grbl 1.1h ['$' for help]");
        Assert.NotEmpty(vm.ConsoleLines);

        vm.ClearCommand.Execute(null);

        Assert.Empty(vm.ConsoleLines);
        Assert.Equal(string.Empty, vm.ConsoleText);
    }

    [Fact]
    public void ConsoleText_joins_lines_for_copyable_output()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());

        grbl.Lines.OnNext("[VER:1.2i.20260521:]");
        grbl.Lines.OnNext("[AXS:5:XYZAB]");
        grbl.Lines.OnNext("[G54:0.000,0.000,0.000,0.000,0.000]");

        // The console is one selectable text blob (ioSender binds its read-only
        // TextBox to the joined ResponseLog) so the dump can be copied as-is.
        Assert.Equal(
            "[VER:1.2i.20260521:]" + Environment.NewLine +
            "[AXS:5:XYZAB]" + Environment.NewLine +
            "[G54:0.000,0.000,0.000,0.000,0.000]",
            vm.ConsoleText);
    }

    [Fact]
    public void Log_is_capped_at_200_lines()
    {
        var grbl = new LineGrbl();
        var vm = new ConsoleViewModel(grbl, MakeSettings());

        for (var i = 0; i < 250; i++)
            grbl.Lines.OnNext($"line {i}");

        Assert.Equal(200, vm.ConsoleLines.Count);
        Assert.Equal("line 50", vm.ConsoleLines[0]);
    }

    [Fact]
    public void Options_persist_to_settings()
    {
        var grbl = new LineGrbl();
        var settings = MakeSettings();
        var vm = new ConsoleViewModel(grbl, settings);

        vm.Verbose = true;
        vm.FilterRealtime = true;
        vm.ShowAllRealtime = true;

        var reloaded = ConsoleSettingsStore.Load(new SettingsService(settings.SettingsDir).Load());
        Assert.True(reloaded.Verbose);
        Assert.True(reloaded.FilterRealtime);
        Assert.True(reloaded.ShowAllRealtime);
    }

    [Fact]
    public void Services_without_raw_line_stream_show_nothing()
    {
        var vm = new ConsoleViewModel(new SilentGrbl(), MakeSettings());
        Assert.Empty(vm.ConsoleLines);
    }
}
