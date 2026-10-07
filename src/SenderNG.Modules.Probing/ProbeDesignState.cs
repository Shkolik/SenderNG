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

using Avalonia;
using Avalonia.Controls;
using SenderNG.Modules.Probing.Core;

namespace SenderNG.Modules.Plugins;

/// <summary>
/// Design-mode panel pinning for <see cref="ProbeView"/>. The Avalonia
/// previewer instantiates the view without a DataContext, so the six
/// IsVisible bindings fail and every tool panel stacks on top of the others.
/// Setting <c>PinnedTool</c> on the ProbeView root in XAML selects which
/// panel the designer shows:
/// <code>&lt;panels:ProbeDesignState.PinnedTool="CenterFinder"&gt;</code>
/// ProbeView's code-behind reads it and hands it to
/// <see cref="ProbeViewModel.CreateDesignInstance"/>. Only the previewer
/// path consults this property; runtime behavior is untouched.
/// </summary>
public static class ProbeDesignState
{
    public static readonly AttachedProperty<ProbingType> PinnedToolProperty =
        AvaloniaProperty.RegisterAttached<Control, ProbingType>("PinnedTool", typeof(ProbeDesignState), ProbingType.ToolLength);

    public static ProbingType GetPinnedTool(Control control) => control.GetValue(PinnedToolProperty);

    public static void SetPinnedTool(Control control, ProbingType value) => control.SetValue(PinnedToolProperty, value);
}