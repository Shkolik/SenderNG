
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

namespace SenderNG.Core.Modules.Probing;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// One named probing profile. Field set mirrors ioSender's
/// <c>CNC.Controls.Probing.ProbingProfile</c> so a user's muscle memory and
/// defaults carry over 1:1.
/// </summary>
public class ProbingProfile : INotifyPropertyChanged
{
    private string _name = "<Default>";

    /// <summary>Display name, e.g. "&lt;Default&gt;".</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Probe/tool tip diameter (mm). Used for edge/corner compensation.</summary>
    public double ProbeDiameter { get; set; } = 2d;

    /// <summary>Touch plate height above the workpiece top (mm).</summary>
    public double TouchPlateHeight { get; set; } = 1d;

    /// <summary>Fixture height used when probing a fixture (mm).</summary>
    public double FixtureHeight { get; set; } = 1d;

    /// <summary>Feed rate for rapid positioning moves; 0 = use the controller setting (G0).</summary>
    public double RapidsFeedRate { get; set; }

    /// <summary>Feed rate for the first (search) probing move (mm/min).</summary>
    public double ProbeFeedRate { get; set; } = 100d;

    /// <summary>Feed rate for the second (latch) probing move (mm/min).</summary>
    public double LatchFeedRate { get; set; } = 25d;

    /// <summary>Maximum distance of a probing move (mm).</summary>
    public double ProbeDistance { get; set; } = 10d;

    /// <summary>Retract distance before the slow latch probe; 0 = skip the latch pass (mm).</summary>
    public double LatchDistance { get; set; } = 0.5d;

    /// <summary>Initial distance away from the face to probe (mm).</summary>
    public double XYClearance { get; set; } = 5d;

    /// <summary>Distance from the corner to the probe point (mm).</summary>
    public double Offset { get; set; } = 5d;

    /// <summary>Z offset below the start position where XY probing takes place (mm).</summary>
    public double Depth { get; set; } = 3d;

    /// <summary>X offset from the probe to the controlled point (mm).</summary>
    public double ProbeOffsetX { get; set; }

    /// <summary>Y offset from the probe to the controlled point (mm).</summary>
    public double ProbeOffsetY { get; set; }

    /// <summary>Offsets are the horizontal thickness of a touch plate.</summary>
    public bool TouchPlateIsXY { get; set; }

    /// <summary>A fresh profile seeded with ioSender's &lt;Default&gt; values.</summary>
    public static ProbingProfile CreateDefault() => new()
    {
        Name = "<Default>",
        RapidsFeedRate = 0d,
        ProbeFeedRate = 100d,
        LatchFeedRate = 25d,
        ProbeDistance = 10d,
        LatchDistance = 0.5d,
        ProbeDiameter = 2d,
        XYClearance = 5d,
        Offset = 5d,
        ProbeOffsetX = 0d,
        ProbeOffsetY = 0d,
        Depth = 3d,
        TouchPlateHeight = 1d,
        FixtureHeight = 1d,
        TouchPlateIsXY = false,
    };
}
