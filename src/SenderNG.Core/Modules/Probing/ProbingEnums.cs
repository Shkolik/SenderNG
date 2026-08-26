namespace SenderNG.Core.Modules.Probing;

/// <summary>Probing tools; values mirror ioSender's <c>ProbingType</c>.</summary>
public enum ProbingType
{
    None = 0,
    ToolLength,
    EdgeFinderInternal,
    EdgeFinderExternal,
    CenterFinder,
    Rotation,
    HeightMap,
}

/// <summary>Edge/corner selector (topology diagram, as in ioSender):</summary>
/// <code>
/// D |-----| C
///   |  Z  |
/// A |-----| B
/// </code>
public enum Edge
{
    None = 0,
    A,
    B,
    C,
    D,
    Z,
    AB,
    AD,
    CB,
    CD,
}

/// <summary>Center finder mode: probe the inside walls or the outside of a workpiece.</summary>
public enum Center
{
    None = 0,
    Inside,
    Outside,
}

/// <summary>How the probed position is written to the controller.</summary>
public enum CoordinateMode
{
    /// <summary>Write the work coordinate system via G10 L2 Pn (or G10 L20 when relative).</summary>
    G10 = 0,
    /// <summary>Set the current position via G92.</summary>
    G92,
    /// <summary>Do not touch the controller; report the measured position only.</summary>
    Measure,
}

/// <summary>Work coordinate system selector, 1..6 → G54..G59 (ioSender's CsSelectControl).</summary>
public enum CoordinateSystem
{
    G54 = 1,
    G55 = 2,
    G56 = 3,
    G57 = 4,
    G58 = 5,
    G59 = 6,
}
