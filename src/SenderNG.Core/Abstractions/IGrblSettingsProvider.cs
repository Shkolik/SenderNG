
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
/// Provides raw grbl setting lines from the controller. The plain <c>$$</c> dump is
/// universal; the <c>$ES</c> (setting metadata) / <c>$EG</c> (group tree) / <c>$SED=</c>
/// (per-setting description) queries are grblHAL-only — providers that can't answer
/// (classic grbl, demo without a catalog) fall back to the default empty responses.
/// </summary>
public interface IGrblSettingsProvider
{
    /// <summary><c>$$</c> dump: "<c>$N=value</c>" lines (all firmwares).</summary>
    Task<IReadOnlyList<string>> GetSettingLinesAsync();

    /// <summary>
    /// <c>$ES</c> dump: "<c>[SETTING:id|group|name|unit|type|format|min|max|reboot|allowNull]</c>"
    /// lines (grblHAL with ENUMS only; empty otherwise).
    /// </summary>
    Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    /// <summary>
    /// <c>$EG</c> dump: "<c>[SETTINGGROUP:id|parent|name]</c>" lines — the settings tree
    /// (grblHAL with ENUMS only; empty otherwise).
    /// </summary>
    Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    /// <summary>
    /// <c>$SED=&lt;id&gt;</c>: long description for one setting (grblHAL with SED only;
    /// null when the firmware has none). Descriptions arrive as
    /// "<c>[SETTINGDESCR:id|text]</c>" with literal <c>\n</c> escapes.
    /// </summary>
    Task<string?> GetSettingDescriptionAsync(int id) =>
        Task.FromResult<string?>(null);
}
