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

using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Plugins;

/// <summary>Probe-panel hotkeys. Module-scoped: they only fire while the probe
/// view has focus, so they can reuse <c>Alt+R</c>/<c>Alt+S</c> alongside the global
/// cycle controls (ioSender's contextual behavior).</summary>
internal static class ProbingHotkeyProvider
{
    private const string ModuleId = "probing";

    public static IReadOnlyList<IHotkeyAction> Create(IServiceProvider services)
    {
        ProbeViewModel Probe() => services.GetRequiredService<ProbeViewModel>();

        return
        [
            new CommandHotkeyAction(
                "probe.start", "Start probing", "module.probing.hotkey.probe.start",
                () => Probe().StartCommand.Execute(null),
                () => Probe().StartCommand.CanExecute(null),
                scope: HotkeyScope.Module, moduleId: ModuleId),
            new CommandHotkeyAction(
                "probe.stop", "Stop probing", "module.probing.hotkey.probe.stop",
                () => Probe().StopCommand.Execute(null),
                () => Probe().StopCommand.CanExecute(null),
                scope: HotkeyScope.Module, moduleId: ModuleId),
            new CommandHotkeyAction(
                "probe.connected", "Probe connected", "module.probing.hotkey.probe.connected",
                () => { },
                () => false,
                scope: HotkeyScope.Module, moduleId: ModuleId),
        ];
    }
}