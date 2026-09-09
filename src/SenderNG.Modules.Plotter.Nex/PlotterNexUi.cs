/*
 * PlotterNexUi.cs - Plugin-local UI-thread marshaling
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

using Avalonia.Threading;

namespace SenderNG.Modules.Plotter.Nex;

/// <summary>
/// Plugin-local UI-thread marshaling, mirroring the app's <c>UiThread</c>
/// semantics (which a plugin cannot reference): the dispatcher is captured at
/// <see cref="Initialize"/> — called by <see cref="PlotterNexModule.CreateViewModel"/>,
/// which the host invokes on the UI thread — and callbacks post through it.
/// A host that never initializes it (headless/unit-test construction of the
/// view model directly) runs callbacks inline, keeping tests deterministic.
/// </summary>
public static class PlotterNexUi
{
    private static Dispatcher? _uiDispatcher;

    /// <summary>Bind to the Avalonia UI dispatcher. Call once from the UI thread.</summary>
    public static void Initialize() => _uiDispatcher = Dispatcher.UIThread;

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, or inline when no
    /// dispatcher was initialized (headless tests) or already on it.
    /// </summary>
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = _uiDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Post(action);
    }
}