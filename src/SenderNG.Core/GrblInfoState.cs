/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

Redistributions of source code must retain the above copyright notice, this list
of conditions and the following disclaimer. Redistributions in binary form must
reproduce the above copyright notice, this list of conditions and the following
disclaimer in the documentation and/or other materials provided with the
distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System;

namespace CNC.Core;

/// <summary>Per-connection controller identity/capability state. A connect
/// (RealGrblService.applyCapabilities) writes one of these so the axes/count/HAL
/// answers can vary per connection; an instance is injected into the services and
/// viewmodels that need it, and tests construct isolated instances. This replaces
/// writing the process-global <see cref="GrblInfo"/> static, which could only be
/// read at defined points by the parser and expressions once a connection is
/// bound. In the app the DI container holds one singleton for everyone (the
/// parser etc. keep reading the static facade that points at it); tests never
/// touch the ambient instance.</summary>
public class GrblInfoState
{
    private int _numAxes = 3;

    public string AxisLetters { get; set; } = GrblInfo.DefaultAxisLetters;

    /// <summary>
    /// CAM letter aliases for the active profile (letter → grbl axis index),
    /// pushed by the kinematics provider from <c>MachineKinematics.AliasMap</c>.
    /// A program letter the controller's <see cref="AxisLetters"/> does not know is
    /// resolved through this map before being reported as unknown — it exists
    /// because a CAM may name an axis the board names differently. Inbound only:
    /// it never affects how an axis is displayed, and it never overrides a letter
    /// the board does report.
    /// </summary>
    public IReadOnlyDictionary<char, int> AxisAliases { get; set; } =
        new Dictionary<char, int>();
    public bool IsGrblHAL { get; set; }
    public int NumAxes
    {
        get => _numAxes;
        set => _numAxes = value;
    }

    /// <summary>The letter at an axis index. Throws once the index reaches the reported
    /// letter set (a short <c>[AXS:4:XYZA]</c> board against a nine-axis walk) — a caller
    /// rendering an arbitrary index wants <see cref="AxisLetters.LetterAt"/> instead,
    /// which yields <c>'?'</c>.</summary>
    public string AxisIndexToLetter(int index) => AxisLetters.Substring(index, 1);

    public int AxisLetterToIndex(string letter) => AxisLetters.IndexOf(letter);
}