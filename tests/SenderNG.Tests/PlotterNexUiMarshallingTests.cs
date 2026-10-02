/*
 * PlotterNexUiMarshallingTests.cs - Nex UI marshalling initialization
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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using CNC.Core.Abstractions;
using SenderNG.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Plotter.Nex;
using CNC.Core.Services;
using Xunit;

namespace SenderNG.Tests;

public class PlotterNexUiMarshallingTests
{
    /// <summary>
    /// Audit §2.1: <c>PlotterNexUi.Initialize</c> had no caller, so <c>Post</c> took
    /// its inline branch and ran view-model callbacks on the serial consumer
    /// thread. Pinning it through the public <c>Post</c> API - no reflection into
    /// the private static - because the observable defect is "ran on the wrong
    /// thread", not "the field is null".
    ///
    /// Uses <c>[AvaloniaFact]</c> so a valid <c>Dispatcher.UIThread</c> exists;
    /// the background thread below then has no access to it, which is what makes
    /// "queued, not run inline" observable.
    ///
    /// <para>STATIC STATE WARNING: <see cref="PlotterNexUi"/> captures a
    /// process-wide dispatcher via <see cref="PlotterNexUi.Initialize"/>.
    /// Because <c>_uiDispatcher</c> is private and the design forbids
    /// reflection/InternalsVisibleTo (and widening the field to internal for
    /// test-only reasons is not permitted), there is no safe way to save/restore
    /// it here. Any future <c>SenderNG.Tests</c> test that constructs a
    /// <see cref="SenderNG.Modules.Plotter.Nex.PlotterNexViewModel"/> MUST call
    /// <see cref="PlotterNexUi.Initialize"/> on its own UI thread first, or it
    /// will inherit this test's dispatcher (which may be torn down) — a hazard
    /// already documented for the Skia plotter (see
    /// <c>PlotterModuleSettingsHeadlessTests</c> remarks and
    /// <c>PlotterTestStubs.MakeVm</c>).</para>
    /// </summary>
    [AvaloniaFact]
    public void CreateViewModel_initializes_the_marshaller()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nex-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new SettingsService(dir);
            var services = new ServiceCollection()
                .AddSingleton<ILoadedProgramSource>(new PlotterTestStubs.StubProgramSource())
                .AddSingleton<IGrblService>(new DemoGrblService(settings.Load()))
                .AddSingleton(settings)
                .AddSingleton(new LocalizationService(
                    new MemoryStream("{ \"strings\": {} }"u8.ToArray())))
                .BuildServiceProvider();

            // Non-vacuity guard: proves the detector above can actually see the
            // bug before the fix, so a later "Assert.False" cannot pass merely
            // because the callback never fires.
            Assert.True(RunsInlineOffThread(),
                "Pre-condition: PlotterNexUi.Post runs inline off-thread until Initialize() is called");

            new PlotterNexModule().CreateViewModel(services);

            // The regression: with no dispatcher captured, Post ran inline here.
            Assert.False(RunsInlineOffThread());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>True when <c>PlotterNexUi.Post</c> executed the callback on the
    /// calling (background) thread instead of queueing it.</summary>
    private static bool RunsInlineOffThread()
    {
        var executed = new ManualResetEventSlim(false);
        Task.Run(() => PlotterNexUi.Post(() => executed.Set()));
        // The buggy path sets this immediately. The fixed path queues onto a
        // dispatcher that nothing pumps in a test, so it cannot fire. A generous
        // timeout therefore cannot produce a false pass - only a false failure on
        // a pathologically slow machine.
        return executed.Wait(TimeSpan.FromMilliseconds(500));
    }
}