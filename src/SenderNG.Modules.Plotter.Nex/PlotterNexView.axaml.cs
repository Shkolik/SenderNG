/*
 * PlotterNexView.axaml.cs - HelixToolkit.Nex (Vulkan) host for the plotter
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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Avalonia;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Modules.Plotter.Nex;

/// <summary>
/// Hosts the Vulkan plot scene. The engine and scene are expensive to build
/// and must not block the UI thread (the Nex scene build is synchronous-over-
/// asynchronous), so creation runs on the thread pool and the result is applied
/// back on the UI thread. A failed creation (no Vulkan 1.3 device, missing
/// runtime) flips <see cref="PlotterNexViewModel.VulkanUnavailable"/> and the
/// XAML swaps in the text fallback.
/// <para>
/// Preset snap buttons re-apply the scene camera framing and then re-raise
/// <see cref="CameraController"/> so the viewport binds the new active
/// (camera, controller) pair.
/// </para>
/// </summary>
public partial class PlotterNexView : UserControl, INotifyPropertyChanged
{
    private PlotterNexViewModel? _vm;
    private NexPlotScene? _scene;
    private int _sceneGeneration;

    public PlotterNexView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        FallbackTitle.Text = LocalizationService.Current?.Resolve(
            "module.plotterNex.vulkanUnavailable", "3D plotting unavailable") ?? "3D plotting unavailable";
        FallbackHint.Text = LocalizationService.Current?.Resolve(
            "module.plotterNex.vulkanUnavailableHint",
            "No Vulkan 1.3 device could be created — 2D plotting stays available.")
            ?? "No Vulkan 1.3 device could be created — 2D plotting stays available.";
    }

    /// <summary>The live Nex engine the viewport renders with.</summary>
    public Engine? Engine => _scene?.Engine;

    /// <summary>Per-frame camera + scene data for the viewport.</summary>
    public IViewportClient? ViewportClient => _scene?.ViewportClient;

    /// <summary>The active camera controller (the orbit pair for 3D, the
    /// rotation-locked pair for the orthographic presets).</summary>
    public ICameraController? CameraController => _scene?.ActiveCameraController;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as PlotterNexViewModel;
        if (_vm is null)
            return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        EnsureScene();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlotterNexViewModel.VulkanUnavailable)
            && _vm!.VulkanUnavailable && _scene is not null)
        {
            // Engine built fine but something downstream reported failure:
            // release the GPU resources and surface the fallback.
            var scene = _scene;
            _scene = null;
            scene.Dispose();
            RaiseSceneBindings();
        }
    }

    /// <summary>Build the scene on the thread pool; AttachScene on the UI thread.
    /// The scene object lives as long as this view, so preset re-frames and
    /// program rebuilds mutate it in place.</summary>
    private void EnsureScene()
    {
        if (_vm is null || _scene is not null)
            return;

        var generation = ++_sceneGeneration;
        Task.Run(() =>
        {
            var scene = new NexPlotScene();
            Dispatcher.UIThread.Post(() =>
            {
                if (_vm is null || generation != _sceneGeneration)
                {
                    scene.Dispose();
                    return;
                }
                _scene = scene;
                RaiseSceneBindings();
                try
                {
                    _vm.AttachScene(new KinematicSceneFactory(scene));
                }
                catch
                {
                    // CreateHeadless threw (no Vulkan) or the attach failed.
                    _scene = null;
                    scene.Dispose();
                    RaiseSceneBindings();
                    _vm.VulkanUnavailable = true;
                }
            });
        });
    }

    private void RaiseSceneBindings()
    {
        OnPropertyChanged(nameof(Engine));
        OnPropertyChanged(nameof(ViewportClient));
        OnPropertyChanged(nameof(CameraController));
    }

    private void Snap(ViewPreset preset)
    {
        var scene = _scene;
        if (scene is null)
            return;
        try
        {
            scene.SetViewPreset(preset);
        }
        catch
        {
            _vm!.VulkanUnavailable = true;
            return;
        }
        // The preset switched the active (camera, controller) pair — the
        // viewport must re-bind CameraController to keep pointer input on it.
        OnPropertyChanged(nameof(CameraController));
    }

    private void OnSnapXy(object? sender, RoutedEventArgs e) => Snap(ViewPreset.XY);
    private void OnSnapXz(object? sender, RoutedEventArgs e) => Snap(ViewPreset.XZ);
    private void OnSnapYz(object? sender, RoutedEventArgs e) => Snap(ViewPreset.YZ);
    private void OnSnapIso(object? sender, RoutedEventArgs e) => Snap(ViewPreset.Isometric3D);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}