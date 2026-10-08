/*
 * PlotterGlViewHeadlessTests.cs - attach/detach lifecycle tests for the Plot GL view
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

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plotter.GL;
using Xunit;

namespace SenderNG.Tests;

public class PlotterGlViewHeadlessTests
{
    /// <summary>Tab switch away and back must leave the view able to draw
    /// again.
    /// <para>Detach runs <c>FreeRenderResources</c>, which disposes and nulls
    /// the per-attach GL context while the scene itself deliberately survives
    /// for the view's lifetime. The tick gates on that context
    /// (<c>EnableTick</c> returns early when it is null), so the re-attach
    /// path has to recreate it before re-enabling the tick — otherwise ONE
    /// tab switch away permanently kills rendering: no tick, no frames, a
    /// live scene nothing drives, and the app looks healthy.</para>
    /// <para>Covers the real code path end to end: a headless window actually
    /// attaches and detaches the view (the <c>AttachedToVisualTree</c> /
    /// <c>DetachedFromVisualTree</c> handlers under test), and the state is
    /// read back from the view's own private fields — the same seam the
    /// existing plotter headless tests use for view internals. Every
    /// intermediate state is asserted (detached = context null + tick off)
    /// so a no-op detach or a skipped re-attach cannot make this pass
    /// vacuously.</para></summary>
    [AvaloniaFact]
    public void Attach_detach_attach_recreates_the_gl_context_and_restarts_the_tick()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-view-tests-" + Guid.NewGuid().ToString("N"));
        PlotterGlView? view = null;
        Window? window = null;
        try
        {
            // Stub set the host provides (mirrors PlotterGlModuleTests):
            // ILoadedProgramSource, IGrblService, SettingsService, LocalizationService.
            var settings = new SettingsService(dir);
            using var grbl = new DemoGrblService(settings.Load());
            var services = new ServiceCollection()
                .AddSingleton<ILoadedProgramSource>(new ProgramViewModel())
                .AddSingleton<IGrblService>(grbl)
                .AddSingleton(settings)
                .AddSingleton(new LocalizationService(
                    new MemoryStream("{ \"strings\": {} }"u8.ToArray())))
                .BuildServiceProvider();
            var vm = (PlotterGlViewModel)new PlotterGlModule().CreateViewModel(services);

            // DataContext first: the setter runs EnsureScene, so the scene,
            // context and tick all exist before the first attach.
            view = new PlotterGlView { DataContext = vm };
            window = new Window { Width = 800, Height = 600, Content = view };
            window.Show();

            Assert.True(Private<bool>(view, "_attached"), "first attach did not run");
            AssertRenderable(view, "after the first attach");

            // Tab switch away: per-attach resources are torn down, the scene survives.
            window.Content = null;
            Assert.False(Private<bool>(view, "_attached"), "detach did not run");
            Assert.True(Private(view, "_gl") is null,
                "detach must dispose and null the per-attach GL context");
            Assert.False(Private<bool>(view, "_tickSubscribed"),
                "detach must stop and unsubscribe the tick");
            Assert.False(Assert.IsType<DispatcherTimer>(Private(view, "_tick")).IsEnabled,
                "detach must stop the tick timer");

            // Tab switch back: without recreating _gl, EnableTick bails on its
            // null gate and the tick never restarts — the regression this pins.
            window.Content = view;
            Assert.True(Private<bool>(view, "_attached"), "re-attach did not run");
            AssertRenderable(view, "after re-attach");
        }
        finally
        {
            if (window is not null)
                window.Close();
            (view?.DataContext as PlotterGlViewModel)?.Dispose();
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The two preconditions for the view drawing anything: a live
    /// per-attach context and a running, subscribed tick.</summary>
    private static void AssertRenderable(PlotterGlView view, string stage)
    {
        Assert.True(Private(view, "_gl") is not null,
            $"{stage}: no GL context — nothing for the tick to drive");
        Assert.True(Private<bool>(view, "_tickSubscribed"),
            $"{stage}: tick handler is not subscribed");
        var tick = Assert.IsType<DispatcherTimer>(Private(view, "_tick"));
        Assert.True(tick.IsEnabled, $"{stage}: tick timer is not running");
    }

    private static object? Private(object instance, string name) =>
        Field(instance, name).GetValue(instance);

    private static T Private<T>(object instance, string name) =>
        (T)(Private(instance, name)
            ?? throw new InvalidOperationException($"field {name} on {instance.GetType().Name} is null"));

    private static FieldInfo Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"field {name} not found on {instance.GetType().Name} — the test seam moved");
}
