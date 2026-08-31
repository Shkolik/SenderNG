
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

using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SenderNG.Core.Modules;

public enum PanelHint { Left, Center, Right, Flyout, Menu, Bottom }

/// <summary>
/// Contract for a pluggable sender module. Core stays UI-agnostic: view/view-model
/// creation returns <see cref="object"/> and the host (Avalonia) casts them.
/// </summary>
public interface IModule
{
    /// <summary>Unique, stable identifier used for settings and routing.</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string Name { get; }

    /// <summary>One-line summary shown in App Settings; localized via <c>module.&lt;id&gt;.description</c>
    /// when a key exists in the active locale, falling back to this literal string.</summary>
    string Description => string.Empty;

    /// <summary>Sort order in which the module's UI is presented.</summary>
    int Order { get; }

    /// <summary>Preferred panel where the module's UI should be hosted.</summary>
    PanelHint Panel => PanelHint.Center;

    /// <summary>Whether the module is visible by default (overridable via AppSettings.ModulesVisibility).</summary>
    bool IsVisibleByDefault => true;

    /// <summary>Whether the user may hide this module. Fixed layout modules (DRO, Program, Console, Jog) cannot.</summary>
    bool CanHide => true;

    /// <summary>Global hot keys provided by this module.</summary>
    IReadOnlyList<HotKey> HotKeys { get; }

    /// <summary>Registers module-provided services into the shared DI container.</summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>Loads/initializes module settings from the shared application settings.</summary>
    void RegisterSettings(AppSettings settings);

    /// <summary>Creates the module's view model. UI-agnostic (returns object).</summary>
    object CreateViewModel(IServiceProvider serviceProvider);

    /// <summary>Creates the module's view bound to the given view model. UI-agnostic.</summary>
    object CreateView(object viewModel);

    /// <summary>Preferred (width, height) when the module opens as a dialog; null = size to content.</summary>
    (double Width, double Height)? DialogPreferredSize => null;
}
