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

namespace CNC.Core.Services;

/// <summary>
/// App-level settings (<c>global.json</c>) — nothing machine-coupled lives
/// here. Holds the UI language, the active-profile pointer, the recent-file
/// list, the network-availability feature gate, the profile registry (id →
/// display name) and global-scoped module sections (currently only Console).
/// See <c>docs/settings-migration.md</c>.
/// </summary>
public class GlobalSettings
{
    /// <summary>App-level UI language (locale code, e.g. "en"/"ru"); empty = English.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Active profile id (<c>profiles/&lt;id&gt;.json</c>); replaces the legacy Mode value.</summary>
    public string ActiveProfileId { get; set; } = MachineProfile.DefaultId;

    /// <summary>Recent G-code files, most recent first; persists across app restarts (app-level).</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Cap for <see cref="RecentFiles"/> (MainViewModel hardcoded 10; default 10, clamp ≥ 1).</summary>
    public int MaxRecentFiles { get; set; } = 10;

    /// <summary>Network connect (grblHAL over TCP) availability — gates the Connect dialog's Network tab.</summary>
    public bool NetworkAvailable { get; set; } = true;

    /// <summary>Profile registry: id → display name (sorted list for the profile manager UI).</summary>
    public Dictionary<string, string> Profiles { get; set; } = new();

    /// <summary>Global-scoped module sections (Console options today); the rest live per profile.</summary>
    public Dictionary<string, object> ModuleSettings { get; set; } = new();

    /// <summary>User keymap: hotkey action id → canonical gesture string.
    /// Absent = the action's default; <c>"(none)"</c>/empty = deliberately unbound.
    /// App-level (a machine swap must not change the keyboard).</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();
}
