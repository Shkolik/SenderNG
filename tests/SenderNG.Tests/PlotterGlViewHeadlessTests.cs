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
    static PlotterGlViewHeadlessTests()
    {
        // Same test-only shim as GlFactAttribute (SenderNG.Core.Tests): pin
        // gdi32's bare-name opengl32 lookup to the SYSTEM module before any
        // GL call, so ChoosePixelFormat works while .NET's DllImport probing
        // still binds the app-local Mesa deployment in this bin folder (the
        // only GL 3.3+ this headless session can offer). No-op on machines
        // without the app-local deployment. Must run before the first
        // GlContext.Create in the process — the static constructor guarantees
        // that for this class. See GlFactAttribute.PreloadSystemOpenGl for
        // the full mechanism.
        //
        // Same second job as that method: pin Mesa's Gallium driver to
        // llvmpipe unless the caller chose one — the app-local Mesa build's
        // d3d12 Gallium driver crashes the process the first time a primitive
        // draw executes (the scene draws its chrome every tick as of Task 4).
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GALLIUM_DRIVER")))
            Environment.SetEnvironmentVariable("GALLIUM_DRIVER", "llvmpipe");
        var system = Path.Combine(Environment.SystemDirectory, "opengl32.dll");
        if (File.Exists(system))
            LoadLibraryW(system);
    }

    [System.Runtime.InteropServices.DllImport("kernel32",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint LoadLibraryW(string path);

    /// <summary>Tab switch away and back must leave the view able to draw
    /// again.
    /// <para>Detach runs <c>FreeRenderResources</c>, which tears down the
    /// whole per-attach render state — readback, scene and GL context together,
    /// because the scene's GL objects only exist on the context that created
    /// them (the view model's factory is released with it). The tick gates on
    /// that context (<c>EnableTick</c> returns early when it is null), so the
    /// re-attach path has to rebuild scene + context before re-enabling the
    /// tick — otherwise ONE tab switch away permanently kills rendering: no
    /// tick, no frames, and the app looks healthy.</para>
    /// <para>Covers the real code path end to end: a headless window actually
    /// attaches and detaches the view (the <c>AttachedToVisualTree</c> /
    /// <c>DetachedFromVisualTree</c> handlers under test), and the state is
    /// read back from the view's own private fields — the same seam the
    /// existing plotter headless tests use for view internals. Every
    /// intermediate state is asserted (detached = context null + tick off)
    /// so a no-op detach or a skipped re-attach cannot make this pass
    /// vacuously.</para>
    /// <para>Skipped at runtime when this session cannot produce a GL 3.3
    /// context (see <see cref="SkipWithoutHardwareGl"/>); the GPU-gated
    /// Core.Tests cover the driver-facing half.</para></summary>
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
                .AddSingleton<IMachineKinematicsProvider>(
                    sp => new MachineKinematicsProvider(
                        sp.GetRequiredService<SettingsService>(),
                        sp.GetRequiredService<IGrblService>()))
                .BuildServiceProvider();
            var vm = (PlotterGlViewModel)new PlotterGlModule().CreateViewModel(services);

            // DataContext first: the setter runs EnsureScene, so the scene,
            // context and tick all exist before the first attach.
            view = new PlotterGlView { DataContext = vm };
            window = new Window { Width = 800, Height = 600, Content = view };
            window.Show();

            Assert.True(Private<bool>(view, "_attached"), "first attach did not run");
            SkipWithoutHardwareGl(view, vm);
            AssertRenderable(view, "after the first attach");

            // Tab switch away: readback, scene, context and tick are torn
            // down together (one lifetime — see the class docs on the view).
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

    /// <summary>Scene and context are one lifetime: detach must dispose BOTH
    /// (scene before context — its GL objects only delete through a live
    /// context) and release the view model's factory, and re-attach must build
    /// a NEW scene bound to the NEW context.
    /// <para>Pins the reconciliation that keeps GL objects from outliving their
    /// context: <c>FreeRenderResources</c> nulls <c>_scene</c> together with
    /// <c>_gl</c> and calls <c>ReleaseScene</c> — without it the re-attach
    /// <c>AttachScene</c> would throw "A scene factory is already attached" —
    /// and <c>EnsureScene</c> rebuilds both, so the second scene's
    /// <c>Context</c> is the second context, never the disposed first one (a
    /// stale reference would draw on a dead context: every GL call fails and
    /// the tab goes blank).</para>
    /// <para>Skipped at runtime when this session cannot produce a GL 3.3
    /// context — see <see cref="SkipWithoutHardwareGl"/>.</para></summary>
    [AvaloniaFact]
    public void Detach_disposes_scene_and_context_together_and_reattach_builds_a_new_pair()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-view-tests-" + Guid.NewGuid().ToString("N"));
        PlotterGlView? view = null;
        Window? window = null;
        try
        {
            var settings = new SettingsService(dir);
            using var grbl = new DemoGrblService(settings.Load());
            var services = new ServiceCollection()
                .AddSingleton<ILoadedProgramSource>(new ProgramViewModel())
                .AddSingleton<IGrblService>(grbl)
                .AddSingleton(settings)
                .AddSingleton(new LocalizationService(
                    new MemoryStream("{ \"strings\": {} }"u8.ToArray())))
                .AddSingleton<IMachineKinematicsProvider>(
                    sp => new MachineKinematicsProvider(
                        sp.GetRequiredService<SettingsService>(),
                        sp.GetRequiredService<IGrblService>()))
                .BuildServiceProvider();
            var vm = (PlotterGlViewModel)new PlotterGlModule().CreateViewModel(services);

            view = new PlotterGlView { DataContext = vm };
            window = new Window { Width = 800, Height = 600, Content = view };
            window.Show();

            SkipWithoutHardwareGl(view, vm);
            var scene1 = Private(view, "_scene");
            var gl1 = Private(view, "_gl");
            Assert.NotNull(scene1);
            Assert.NotNull(gl1);
            Assert.Same(gl1, ContextOf(scene1));
            Assert.Same(scene1, vm.Scene);

            // Tab switch away: scene + context go together, and the factory
            // is released (without that, re-attach's AttachScene throws).
            window.Content = null;
            Assert.True(Private(view, "_scene") is null,
                "detach must dispose and null the scene together with its context");
            Assert.True(Private(view, "_gl") is null,
                "detach must dispose and null the per-attach GL context");
            Assert.True(vm.Scene is null,
                "detach must release the VM's factory, or re-attach throws 'already attached'");

            // Tab switch back: a fresh scene bound to a fresh context — never
            // the disposed first pair.
            window.Content = view;
            var scene2 = Private(view, "_scene");
            var gl2 = Private(view, "_gl");
            Assert.NotNull(scene2);
            Assert.NotNull(gl2);
            Assert.NotSame(scene1, scene2);
            Assert.NotSame(gl1, gl2);
            Assert.Same(gl2, ContextOf(scene2));
            Assert.Same(scene2, vm.Scene);
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

    /// <summary>The task's core deliverable: one tick renders the solid
    /// background color into the bitmap <c>GpuImage</c> shows. Drives
    /// <c>OnTick</c> synchronously via reflection instead of waiting for the
    /// DispatcherTimer — the handler body (readback create, make-current,
    /// clear, FBO row copy, screenshot mirror) is what this task adds; the
    /// timer's subscribe/enable plumbing is already pinned by the attach
    /// test. Two ticks: the second must run on the SAME readback (create-once
    /// bookkeeping) and advance the tick counter again.</summary>
    /// <para>Skipped at runtime when this session cannot produce a GL 3.3
    /// context — see <see cref="SkipWithoutHardwareGl"/>.</para></summary>
    [AvaloniaFact]
    public void Tick_renders_the_solid_background_color_into_the_bitmap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-view-tests-" + Guid.NewGuid().ToString("N"));
        PlotterGlView? view = null;
        Window? window = null;
        try
        {
            var settings = new SettingsService(dir);
            using var grbl = new DemoGrblService(settings.Load());
            var services = new ServiceCollection()
                .AddSingleton<ILoadedProgramSource>(new ProgramViewModel())
                .AddSingleton<IGrblService>(grbl)
                .AddSingleton(settings)
                .AddSingleton(new LocalizationService(
                    new MemoryStream("{ \"strings\": {} }"u8.ToArray())))
                .AddSingleton<IMachineKinematicsProvider>(
                    sp => new MachineKinematicsProvider(
                        sp.GetRequiredService<SettingsService>(),
                        sp.GetRequiredService<IGrblService>()))
                .BuildServiceProvider();
            var vm = (PlotterGlViewModel)new PlotterGlModule().CreateViewModel(services);

            view = new PlotterGlView { DataContext = vm };
            window = new Window { Width = 800, Height = 600, Content = view };
            window.Show();

            SkipWithoutHardwareGl(view, vm);

            InvokeOnTick(view);
            InvokeOnTick(view);

            Assert.True(!vm.RenderUnavailable,
                "the ticks must not report a render failure");
            var ticks = Private<int>(view, "_readbackTicks");
            Assert.True(ticks == 2, $"both ticks must complete (readbackTicks={ticks})");
            var bitmap = Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(
                Private(view, "_bitmap"));
            // RGBA8888 byte order: the clear color {0.98, 0.98, 0.98, 1}
            // (bytes 250, 250, 250 — checkpoint-1 near-white ruling) must
            // round-trip byte-exact through glClear → glReadPixels →
            // the locked bitmap.
            using var fb = bitmap.Lock();
            var pixel = new byte[4];
            for (var i = 0; i < 4; i++)
                pixel[i] = System.Runtime.InteropServices.Marshal.ReadByte(fb.Address, i);
            Assert.Equal(new byte[] { 250, 250, 250, 255 }, pixel);
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

    private static void InvokeOnTick(PlotterGlView view) =>
        view.GetType()
            .GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, [null, EventArgs.Empty]);

    /// <summary>Skip (at runtime, xunit v3) when this session cannot produce
    /// a GL 3.3 context: the view then caches a failure verdict instead of
    /// <c>_gl</c>, and everything the test would assert depends on a driver
    /// this machine/session does not expose (e.g. GDI Generic 1.1 behind a
    /// remote-desktop session). Asserts that the absence is the KNOWN verdict
    /// path first, so a plain wiring bug (neither context nor verdict) still
    /// fails loudly instead of skipping.</summary>
    private static void SkipWithoutHardwareGl(PlotterGlView view, PlotterGlViewModel vm)
    {
        if (Private(view, "_gl") is not null)
            return;
        var verdict = Private(view, "_glVerdict");
        Assert.True(verdict is not null,
            "first attach produced neither a GL context nor a cached failure verdict");
        var detail = vm.GetType()
            .GetProperty("RenderFailureDetail", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(vm) as string;
        Assert.Skip($"no OpenGL 3.3 context in this session (verdict: {verdict}) {detail}");
    }

    /// <summary>Read the scene's internal <c>Context</c> property — the bind
    /// under test (scene ↔ context identity) — via reflection, since the GL
    /// assembly exposes its internals only to SenderNG.Core.Tests.</summary>
    private static object ContextOf(object scene) =>
        scene.GetType().GetProperty("Context", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(scene)
        ?? throw new InvalidOperationException(
            $"GlPlotScene.Context not found — the test seam moved");

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
