/*
 * ModuleErrorReporter.cs - host-side IErrorReporter (dialog + status bar)
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

using CNC.Core.Abstractions;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Services;

/// <summary>
/// Host-side <see cref="IErrorReporterFactory"/>: creates one
/// <see cref="ModuleErrorReporter"/> per discovered module, so each plugin's
/// reporter is bound to its identity (the common handler knows which display
/// name to prefix — docs/plugin-development.md §3.4).
/// </summary>
public sealed class AvaloniaErrorReporterFactory : IErrorReporterFactory
{
    private readonly IFileDialogService _dialogs;
    private readonly StatusSink _status;

    public AvaloniaErrorReporterFactory(IFileDialogService dialogs, StatusSink status)
    {
        _dialogs = dialogs;
        _status = status;
    }

    public IErrorReporter Create(IModule module) => new ModuleErrorReporter(module, _dialogs, _status);
}

/// <summary>
/// The common, core-owned error surface for all modules: Error shows the
/// "[module name] : [text]" dialog (module display name resolved through the
/// locale), Status routes the same text to the status bar. Fire-and-forget —
/// the reporter is a notification channel, callers never await it, and it
/// never throws into the plugin.
/// </summary>
public sealed class ModuleErrorReporter : IErrorReporter
{
    private readonly IModule _module;
    private readonly IFileDialogService _dialogs;
    private readonly StatusSink _status;

    public ModuleErrorReporter(IModule module, IFileDialogService dialogs, StatusSink status)
    {
        _module = module;
        _dialogs = dialogs;
        _status = status;
    }

    public void Error(string text) => _ = ShowErrorAsync(text);

    public void Status(string text) => _status.Set(text);

    private async Task ShowErrorAsync(string text)
    {
        try
        {
            var loc = LocalizationService.Current;
            var title = loc is null ? _module.Name : ModuleNames.Localized(_module, loc);
            await _dialogs.ShowErrorAsync(title, text);
        }
        catch
        {
            // Reporting must never throw into the plugin.
        }
    }
}