/*
 * HpglModule.cs - HPGL (.plt) → g-code converter module
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

namespace SenderNG.Modules.Hpgl;

/// <summary>
/// HPGL plotter-file plugin. Discovered by the host's Modules/ scan — drop
/// the DLL in and the Open dialog advertises *.plt; remove it and the app has
/// no idea HPGL exists. Own locale catalog (module.hpgl.*), own profile-scoped
/// parameters + ShowDialog leaf in App Settings, and the cut/laser selector
/// in the parameters dialog (ShowCutMode).
/// </summary>
public class HpglModule : ConverterModuleBase, IPluginInfo
{
    public string Version => "1.0.0";
    public string Author => "Andrew Shkolik";
    public string License => "BSD-3-Clause";

    public override string Id => "hpgl";
    public override string Name => "HPGL";
    public override string Description => "HPGL (.plt) plotter files → g-code.";
    public override int Order => 61;

    public override string FileType => "HPGL files";
    public override string FileExtensions => "plt";

    /// <summary>HPGL distinguishes spindle cut from laser preamble.</summary>
    public override bool ShowCutMode => true;

    public override IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.hpgl.name"] = "HPGL",
            ["module.hpgl.description"] = "HPGL (.plt) plotter files → g-code.",
            ["module.hpgl.fileType"] = "HPGL files",
        };

    protected override string? Convert(string sourceText, ConverterParameters parameters) =>
        new HpglToGCode().Convert(sourceText, parameters);
}