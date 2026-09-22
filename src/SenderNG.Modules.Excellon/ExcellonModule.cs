/*
 * ExcellonModule.cs - Excellon (.drl/.xln) → g-code converter module
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

using SenderNG.Modules.Shared;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Excellon;

/// <summary>
/// Excellon drill-file plugin. Discovered by the host's Modules/ scan — drop
/// the DLL in and the Open dialog advertises *.drl;*.xln; remove it and the
/// app has no idea Excellon exists. Own locale catalog (module.excellon.*),
/// own profile-scoped parameters + ShowDialog leaf in App Settings.
/// </summary>
public class ExcellonModule : ConverterModuleBase, IPluginInfo
{
    public string Version => "1.0.0";
    public string Author => "Andrew Shkolik";
    public string License => "BSD-3-Clause";

    public override string Id => "excellon";
    public override string Name => "Excellon";
    public override string Description => "Excellon (.drl/.xln) PCB drill files → g-code.";
    public override int Order => 60;

    public override string FileType => "Excellon files";
    public override string FileExtensions => "drl,xln";

    public override IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.excellon.name"] = "Excellon",
            ["module.excellon.description"] = "Excellon (.drl/.xln) PCB drill files → g-code.",
            ["module.excellon.fileType"] = "Excellon files",
        };

    protected override string? Convert(string sourceText, ConverterParameters parameters) =>
        new Excellon2GCode().Convert(sourceText, parameters);
}