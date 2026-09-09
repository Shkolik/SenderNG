/*
 * PlotterNexViewModel.cs - G-code 3D plotter view model
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
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;

namespace SenderNG.Modules.Plotter.Nex;

/// <summary>
/// Placeholder view model for the 3D plotter. Implements
/// <see cref="INotifyPropertyChanged"/> and <see cref="IDisposable"/> to honor
/// the host's lifecycle expectations, but carries no rendering logic yet — the
/// real viewport data flow (geometry building, orbit camera state, live-run
/// overlay) lands in a later task.
/// <para>
/// The constructor takes the same resolved services the module's
/// <c>CreateViewModel</c> obtains; the real tasks will consume them.
/// </para>
/// </summary>
public sealed class PlotterNexViewModel : INotifyPropertyChanged, IDisposable
{
    public PlotterNexViewModel(
        ILoadedProgramSource programSource,
        IGrblService grbl,
        SettingsService settings,
        LocalizationService localization)
    {
        ProgramSource = programSource;
        Grbl = grbl;
        Settings = settings;
        Localization = localization;
    }

    // The services resolved by the module — held for the tasks that wire the
    // real viewport. Intentionally minimal for now.
    public ILoadedProgramSource ProgramSource { get; }
    public IGrblService Grbl { get; }
    public SettingsService Settings { get; }
    public LocalizationService Localization { get; }

#pragma warning disable CS0067 // Placeholder: the event is never raised yet.
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    public void Dispose() { }
}
