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

namespace SenderNG.Modules.Probing;

/// <summary>
/// Marshals callbacks to the Avalonia UI thread. Machine-state emissions can
/// arrive on background threads (demo poll timer, future serial-port reads),
/// but ViewModels touch Avalonia-owned objects (brushes, bound collections)
/// which must be updated on the UI thread.
/// <para>
/// The dispatcher is captured at app startup via <see cref="Initialize"/>;
/// headless/unit-test hosts never initialize it, so callbacks run inline on
/// the calling thread - keeping synchronous test assertions deterministic.
/// </para>
/// </summary>
public static class UiThread
{
    private static Dispatcher? _uiDispatcher;

    /// <summary>Bind to the app's UI dispatcher. Call once from the UI thread (App startup).</summary>
    public static void Initialize() => _uiDispatcher = Dispatcher.UIThread;

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, or inline when already
    /// there (or when no dispatcher was initialized, e.g. unit tests).
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