
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

namespace SenderNG.Core.Modules;

/// <summary>Modules implement this to expose editable fields in App Settings.</summary>
public interface IModuleSettingsProvider
{
    /// <summary>Numeric fields; default none (prefer CreateSettingsView).</summary>
    IEnumerable<ModuleSettingField> GetSettingFields() => [];

    /// <summary>Optional boolean fields (checkboxes); default none.</summary>
    IEnumerable<ModuleSettingBool> GetBoolSettingFields() => [];

    /// <summary>Module-provided settings UI, injected into the App Settings card.
    /// When non-null it replaces the generic field lists.</summary>
    object? CreateSettingsView() => null;

    /// <summary>Called once after host build so the module can resolve services
    /// (settings fields may be read before the module's own UI is ever opened).</summary>
    void AttachServices(IServiceProvider services) { }
}

public sealed class ModuleSettingField(string label, Func<double> get, Action<double> set, string? key = null)
{
    public string Label { get; } = label;
    /// <summary>Optional localization key resolved through LocalizationService (fallback = <see cref="Label"/>).</summary>
    public string? Key { get; } = key;
    public Func<double> Get { get; } = get;
    public Action<double> Set { get; } = set;
}

public sealed class ModuleSettingBool(string label, Func<bool> get, Action<bool> set, string? key = null)
{
    public string Label { get; } = label;
    /// <summary>Optional localization key resolved through LocalizationService (fallback = <see cref="Label"/>).</summary>
    public string? Key { get; } = key;
    public Func<bool> Get { get; } = get;
    public Action<bool> Set { get; } = set;
}
