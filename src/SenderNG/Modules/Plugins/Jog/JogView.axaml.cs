
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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SenderNG.Modules.Plugins;

public partial class JogView : UserControl
{
    public JogView() => InitializeComponent();

    private JogViewModel? Vm => DataContext as JogViewModel;

    private Avalonia.Input.IPointer? _heldPointer;   // only this pointer's release/capture-lost stops the hold

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Tunnel: our press handler runs BEFORE the Button's, so marking the press
        // handled suppresses the Click command in continuous mode.
        AddHandler(InputElement.PointerPressedEvent, OnJogPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerReleasedEvent, OnJogPointerReleased);
        AddHandler(InputElement.PointerCaptureLostEvent, OnJogPointerCaptureLost);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        RemoveHandler(InputElement.PointerPressedEvent, OnJogPointerPressed);
        RemoveHandler(InputElement.PointerReleasedEvent, OnJogPointerReleased);
        RemoveHandler(InputElement.PointerCaptureLostEvent, OnJogPointerCaptureLost);
        // Closing the panel mid-hold: the handlers are gone so no release/capture-lost
        // will follow — cancel the held jog NOW (sends 0x85, clears the stale tag)
        // instead of leaving the machine jogging to travel with the tag stuck.
        _heldPointer = null;
        Vm?.StopContinuousJog();
    }

    // Jog-motion buttons carry a string CommandParameter (tags); the Stop button has
    // none, so the filter naturally excludes it. e.Source may be a descendant of the
    // button (its TextBlock), so walk up the visual tree to the owning Button.
    private void OnJogPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var button = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault();
        if (button?.CommandParameter is not string tag || Vm is not { IsContinuousStep: true })
            return;                       // step mode, Stop button, or non-jog control: untouched
        Vm!.StartContinuousJog(tag);
        if ((Vm.ContinuousActive || Vm.ContinuousStarting) && _heldPointer is null)
            _heldPointer = e.Pointer;     // the pointer that owns the hold
        e.Pointer.Capture(this);          // release/capture-lost always reach us
        e.Handled = true;                 // suppresses the Button's Click command
    }

    private void OnJogPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Scope to the pointer that actually holds the jog: a second finger (or a
        // stylus touch) lifting must NOT stop a hold owned by another pointer.
        if (e.Pointer != _heldPointer)
            return;
        _heldPointer = null;
        Vm?.StopContinuousJog();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnJogPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer != _heldPointer)
            return;
        _heldPointer = null;
        Vm?.StopContinuousJog();
    }
}
