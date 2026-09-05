/*
 * ConverterSettings.cs - per-profile converter module settings (parameters + ShowDialog)
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

using CNC.Core.Services;
using SenderNG.Modules.Common;

namespace SenderNG.Modules.Shared;

/// <summary>
/// Per-profile settings of one converter module: the job parameters plus the
/// <see cref="ShowDialog"/> flag ("ask on open" vs "convert silently with
/// stored values"). Persisted under the module's id in
/// <c>MachineProfile.ModuleSettings</c> — Z heights and tools belong to a
/// machine, not the app.
/// </summary>
public sealed class ConverterSettings
{
    /// <summary>Show the parameters dialog on open (default true). When false
    /// the module converts silently with the stored parameters — the dialog
    /// still appears if no parameters were ever stored (<see cref="HasValues"/>).</summary>
    public bool ShowDialog { get; set; } = true;

    public double ZHome { get; set; } = 25d;
    public double ZRapids { get; set; } = 1d;
    public double ZSafe { get; set; } = 1d;
    public double ZMin { get; set; } = -1.8d;
    public double RPM { get; set; } = 5000d;
    public double ToolDiameter { get; set; } = 3d;
    public double FeedRate { get; set; } = 300d;
    public double PlungeRate { get; set; } = 100d;
    public double ScaleX { get; set; } = 1d;
    public double ScaleY { get; set; } = 1d;
    public bool CutMode { get; set; } = true;

    /// <summary>Whether any parameters were ever accepted in this profile.
    /// A fresh profile shows the dialog even when <see cref="ShowDialog"/> is
    /// somehow false — converting with invented defaults is worse than asking.</summary>
    public bool HasValues { get; set; }

    public ConverterParameters ToParameters(string sourceName) => new()
    {
        ZHome = ZHome,
        ZRapids = ZRapids,
        ZSafe = ZSafe,
        ZMin = ZMin,
        RPM = RPM,
        ToolDiameter = ToolDiameter,
        FeedRate = FeedRate,
        PlungeRate = PlungeRate,
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        CutMode = CutMode,
        SourceName = sourceName,
    };

    public void Apply(ConverterParameters parameters)
    {
        ZHome = parameters.ZHome;
        ZRapids = parameters.ZRapids;
        ZSafe = parameters.ZSafe;
        ZMin = parameters.ZMin;
        RPM = parameters.RPM;
        ToolDiameter = parameters.ToolDiameter;
        FeedRate = parameters.FeedRate;
        PlungeRate = parameters.PlungeRate;
        ScaleX = parameters.ScaleX;
        ScaleY = parameters.ScaleY;
        CutMode = parameters.CutMode;
        HasValues = true;
    }
}

/// <summary>
/// Reads/writes the per-profile settings of one converter module (section key
/// = module id, e.g. "excellon"), built on the SDK's generic
/// <see cref="ModuleSettingsJson"/> store. Missing or corrupt entries fall
/// back to defaults, so a fresh profile works with zero configuration.
/// Internal shared code — not part of the plugin contract.
/// </summary>
public static class ConverterSettingsStore
{
    public static ConverterSettings Load(MachineProfile profile, string moduleId) =>
        ModuleSettingsJson.Load<ConverterSettings>(profile, moduleId) ?? new ConverterSettings();

    public static void Save(MachineProfile profile, string moduleId, ConverterSettings settings) =>
        ModuleSettingsJson.Save(profile, moduleId, settings);
}