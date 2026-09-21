/*
 * IPluginDependencies.cs - optional plugin dependency metadata (third-party libraries)
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

namespace SenderNG.Core.Modules;

/// <summary>
/// Optional metadata a plugin module implements alongside <see cref="IModule"/>
/// (and usually <see cref="IPluginInfo"/>) to declare the third-party libraries
/// the plugin depends on. Shown on the About screen's Modules tab as an
/// expandable "Dependencies" section so a user can assess license terms.
/// Author-supplied and never validated — the platform does not police plugins.
/// </summary>
public interface IPluginDependencies
{
    /// <summary>Third-party libraries this plugin depends on, e.g. "Avalonia v.12.0.0 (MIT)".</summary>
    IReadOnlyList<PluginDependency> Dependencies { get; }
}

/// <summary>One third-party library a plugin depends on.</summary>
public sealed record PluginDependency(string Name, string Version, string License)
{
    /// <summary>One-row display: "Name v.Version · License" (license omitted when blank).</summary>
    public string OneLine { get; } =
        string.IsNullOrEmpty(License)
            ? $"{Name} v.{Version}"
            : $"{Name} v.{Version} · {License}";
}