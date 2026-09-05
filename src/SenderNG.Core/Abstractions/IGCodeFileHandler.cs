/*
 * IGCodeFileHandler.cs - file-format → g-code plugin contract (Excellon .drl, HPGL .plt, ...)
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
/// Contract for a module that opens foreign file formats and converts them to
/// g-code (Excellon `.drl`, HPGL `.plt`, …). The main app knows only this
/// interface: it builds the Open-dialog filters from registered handlers,
/// dispatches by extension, and feeds the converted text through the same
/// engine gate as plain g-code. Everything else — the parameters dialog,
/// per-profile persistence, localization — is the module's own business,
/// resolved from the services provider handed to <see cref="ConvertAsync"/>.
/// </summary>
public interface IGCodeFileHandler
{
    /// <summary>Human-readable type name for the Open dialog filter ("Excellon files").</summary>
    string FileType { get; }

    /// <summary>Comma-separated lowercase extensions without dots ("drl,xln").</summary>
    string FileExtensions { get; }

    /// <summary>
    /// Optional localization key for <see cref="FileType"/> — the host resolves
    /// it through the locale with <see cref="FileType"/> as the English
    /// fallback. Handlers without a key keep their literal label.
    /// </summary>
    string? FileTypeKey => null;

    /// <summary>
    /// Converts source text to g-code. The module may show its parameters
    /// dialog (via <c>IDialogService</c> + <c>IViewFactory</c> resolved from
    /// <paramref name="services"/>) and persist its settings. Returns null when
    /// the user cancels or the conversion fails — the host then reports a
    /// generic failure and keeps the current program untouched.
    /// </summary>
    Task<string?> ConvertAsync(IServiceProvider services, string sourceName, string sourceText);
}