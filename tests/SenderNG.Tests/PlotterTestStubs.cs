/*
 * PlotterTestStubs.cs - the deterministic plotter test fixture, shared.
 *
 * StubProgramSource and MakeVm used to be private members of
 * PlotterViewHeadlessTests, which made them invisible to
 * PlotterLiveRunHeadlessTests - and that file's own Setup(...) is not a
 * substitute: it streams rows from a real DemoGrblService over ~1.2 s, so any
 * assertion built on it inherits timing it does not need. This helper is a
 * pure move: rows are acked by hand, no streaming, no timing.
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
using SenderNG.Modules.Plotter;
using SenderNG.Modules.Plotter.Settings;

namespace SenderNG.Tests;

/// <summary>The shared, deterministic plotter fixture. Both
/// <see cref="PlotterViewHeadlessTests"/> and
/// <see cref="PlotterLiveRunHeadlessTests"/> import it with
/// <c>using static</c>, so every call site kept its original spelling when the
/// types moved out of the first file.</summary>
internal static class PlotterTestStubs
{
    internal sealed class StubProgramSource : ILoadedProgramSource
    {
        private ProgramRowState[] _states = [];
        public GCodeProgram? Program { get; set; }
        public event System.Action? ProgramChanged;
        public event System.Action? RowsChanged;
        public int RowCount => Program?.Entries.Count ?? 0;
        public int SendableCount => Program?.Entries.Count(e => e.IsSendable) ?? 0;
        public int DoneCount => _states.Count(s => s == ProgramRowState.Ok);
        public int TouchedRow { get; private set; }
        public ProgramRowState GetRowState(int index) =>
            index >= 0 && index < _states.Length ? _states[index] : ProgramRowState.Idle;

        public void Load(string gcode)
        {
            Program = GCodeProgram.Parse(gcode);
            _states = new ProgramRowState[Program.Entries.Count];
            TouchedRow = 0;
            ProgramChanged?.Invoke();
        }

        /// <summary>Mark rows <paramref name="from"/>..<paramref name="to"/>
        /// (exclusive) acked and raise RowsChanged — the live-run tick.</summary>
        public void AckRows(int from, int to, ProgramRowState state = ProgramRowState.Ok)
        {
            for (var i = from; i < to && i < _states.Length; i++)
                _states[i] = state;
            TouchedRow = Math.Max(TouchedRow, Math.Min(to - 1, _states.Length - 1));
            RowsChanged?.Invoke();
        }
    }

    private sealed class NeverObservable : IObservable<MachineState>
    {
        public IDisposable Subscribe(IObserver<MachineState> observer) => new Noop();
        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class StubGrbl : IGrblService
    {
        public bool IsConnected => false;
        public ControllerCapabilities Capabilities => ControllerCapabilities.Full with { AxisLetters = "XYZA" };
        public IObservable<MachineState> State => new NeverObservable();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    /// <summary>A view model over a <see cref="StubProgramSource"/>, in a
    /// throwaway settings directory so the machine-type choice is the only
    /// thing the fixture controls.</summary>
    internal static PlotterViewModel MakeVm(StubProgramSource source, string machineTypeId)
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-ui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = new SettingsService(dir);
        var profile = settings.Load();
        profile.MachineTypeId = machineTypeId;
        settings.Save(profile);

        return new PlotterViewModel(source, new StubGrbl(), settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())));
    }
}
