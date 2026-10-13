/*
 * HeightMapViewerViewModel.cs - view model for the height-map 3D dialog
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
using CNC.Core.Abstractions;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.HeightMapViewer;

/// <summary>
/// Backs the 3D height-map dialog. Subscribes <see cref="IHeightMapSource.Snapshot"/>
/// and surfaces the latest layer plus an <see cref="IsEmpty"/> flag so the view can
/// swap in the localized empty state. Emissions are marshalled to the UI thread
/// (<see cref="UiThread"/>) because the view's <c>DispatcherTimer</c> tick and the
/// bound empty-state visibility both read this state on the UI thread.
/// </summary>
public sealed class HeightMapViewerViewModel : INotifyPropertyChanged, IDisposable
{
    private const string EmptyTextKey = "module.heightmap-viewer.empty";
    private const string EmptyTextFallback = "No height map loaded. Probe or load a map first.";

    private readonly LocalizationService? _localization;
    private readonly IDisposable? _subscription;
    private readonly EventHandler? _localeChanged;
    private HeightMapLayer? _layer;
    private bool _disposed;

    public HeightMapViewerViewModel(IHeightMapSource? source, LocalizationService? localization,
        IFileDialogService? fileDialogs = null)
    {
        _localization = localization;
        FileDialogs = fileDialogs;
        if (_localization is not null)
        {
            // The empty-state text is resolved through the live localization
            // service; re-raise it on a language switch so the dialog follows
            // without a restart (same pattern as the view's code-behind texts).
            _localeChanged = (_, _) => OnPropertyChanged(nameof(EmptyText));
            _localization.Changed += _localeChanged;
        }
        _subscription = source?.Snapshot.Subscribe(OnSnapshot);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True while no layer has been published (or the source cleared it).</summary>
    public bool IsEmpty => _layer is null;

    /// <summary>The latest published height map, or null when cleared/not probed.</summary>
    public HeightMapLayer? Layer => _layer;

    /// <summary>Native file dialogs, surfaced for the view's "Save screenshot"
    /// action. The dialog view is created with a parameterless ctor by the view
    /// factory, so the service travels on the view model instead.</summary>
    public IFileDialogService? FileDialogs { get; }

    /// <summary>Localized empty-state message the view binds.</summary>
    public string EmptyText =>
        _localization?.Resolve(EmptyTextKey, EmptyTextFallback) ?? EmptyTextFallback;

    private void OnSnapshot(HeightMapLayer? layer) => UiThread.Post(() => Apply(layer));

    private void Apply(HeightMapLayer? layer)
    {
        if (_disposed)
            return;
        _layer = layer;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Layer));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_localization is not null && _localeChanged is not null)
            _localization.Changed -= _localeChanged;
        _subscription?.Dispose();
    }
}
