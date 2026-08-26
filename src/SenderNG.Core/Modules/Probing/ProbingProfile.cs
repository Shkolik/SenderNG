namespace SenderNG.Core.Modules.Probing;

/// <summary>
/// One named probing profile. Field set mirrors ioSender's
/// <c>CNC.Controls.Probing.ProbingProfile</c> so a user's muscle memory and
/// defaults carry over 1:1.
/// </summary>
public class ProbingProfile
{
    /// <summary>Display name, e.g. "&lt;Default&gt;".</summary>
    public string Name { get; set; } = "<Default>";

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
