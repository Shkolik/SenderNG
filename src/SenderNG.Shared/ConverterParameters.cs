/*
 * ConverterParameters.cs - shared converter job parameters (ioSender JobParameters shape)
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

namespace SenderNG.Modules.Shared;

/// <summary>
/// Job parameters shared by converter modules — the same set ioSender's
/// JobParametersDialog exposes (Z heights, spindle/tool, feeds, XY scale).
/// One instance is persisted per converter module per machine profile, so each
/// converter keeps its own defaults between runs.
/// </summary>
public sealed class ConverterParameters
{
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

    /// <summary>HPGL: spindle cut vs laser preamble. Filename heuristics
    /// (*Paste.plt → laser) win over this when they apply, like ioSender.</summary>
    public bool CutMode { get; set; } = true;

    /// <summary>Name of the source file being converted (set by the module
    /// before Convert; the HPGL converter's cut/laser heuristic reads it).
    /// Per-conversion — never persisted.</summary>
    public string SourceName { get; set; } = string.Empty;

    public ConverterParameters Clone() => (ConverterParameters)MemberwiseClone();
}