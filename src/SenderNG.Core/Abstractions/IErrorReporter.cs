/*
 * IErrorReporter.cs - core-owned plugin error surfacing (docs/plugin-development.md §3.4)
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

using SenderNG.Core.Modules;

namespace CNC.Core.Abstractions;

/// <summary>
/// Core-owned error surfacing for plugins. Error reporting is not each
/// plugin's private UI: the plugin just says what went wrong and the host
/// decides how to surface it, in a way that is common for all modules. The
/// reporter a module resolves is bound to that module (keyed by its
/// <see cref="IModule.Id"/> in the shared DI container), so the host-side
/// handler knows which display name to prefix.
/// </summary>
public interface IErrorReporter
{
    /// <summary>Error dialog: "[module name] : [text]" — the loaded program stays untouched.</summary>
    void Error(string text);

    /// <summary>Same text as a status-bar message instead of a dialog.</summary>
    void Status(string text);
}

/// <summary>
/// Host-side factory for <see cref="IErrorReporter"/> instances. The host
/// (app) registers one implementation; <see cref="SenderNG.Core.Modules.ModuleCatalog"/>
/// calls it once per discovered module so each module gets a reporter bound
/// to its identity.
/// </summary>
public interface IErrorReporterFactory
{
    IErrorReporter Create(IModule module);
}