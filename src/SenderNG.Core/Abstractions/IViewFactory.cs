/*
 * IViewFactory.cs - view-model → view resolution for dialog hosting
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

namespace CNC.Core.Abstractions;

/// <summary>
/// Maps view-model types to their views for dialog hosting. The app seeds it
/// with its own dialogs; 3rd-party modules register theirs at runtime, so a
/// plugin can show a dialog (parameters, wizards, …) without the app knowing
/// its view types. Registration is idempotent — modules may register on every
/// open. UI-agnostic by design (returns <see cref="object"/>), mirroring
/// <c>IModule.CreateView</c>.
/// </summary>
public interface IViewFactory
{
    /// <summary>Maps <typeparamref name="TViewModel"/> to <typeparamref name="TView"/>.</summary>
    void Register<TViewModel, TView>() where TView : class;

    /// <summary>Creates the registered view for <paramref name="viewModel"/> and
    /// binds it as DataContext. Throws when no view is registered.</summary>
    object Create(object viewModel);
}