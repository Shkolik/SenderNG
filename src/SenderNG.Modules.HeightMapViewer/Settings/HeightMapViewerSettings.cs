/*
 * HeightMapViewerSettings.cs - per-profile rendering settings for the viewer
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

using SenderNG.Modules.Common.Render;

namespace SenderNG.Modules.HeightMapViewer.Settings;

/// <summary>Per-profile height-map viewer rendering config
/// (<c>MachineProfile.ModuleSettings["heightmap-viewer"]</c>).</summary>
public sealed class HeightMapViewerSettings
{
    /// <summary>Render the readback target at 2x panel size and let the control
    /// downsample on display, giving hairline lines a 2px footprint. Default ON.</summary>
    public bool Supersample { get; set; } = true;

    /// <summary>Multisample count of the readback target (None / 2x / 4x / 8x) —
    /// the orthogonal axis to <see cref="Supersample"/>. Default None.</summary>
    public MsaaMode MsaaMode { get; set; } = MsaaMode.None;

    /// <summary>Show the X/Y/Z origin axis arrows in the viewport. Default ON.</summary>
    public bool ShowOrigin { get; set; } = true;
}
