
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

using System.Globalization;

namespace CNC.Core.Services;

/// <summary>A coordinate-system offset returned by grbl's $# report.</summary>
public sealed class WorkCoordinateSystem
{
    public WorkCoordinateSystem(string code, int id, int axisCount = 9)
    {
        Code = code;
        Id = id;
        Values = new double[axisCount];
    }

    public string Code { get; }
    public int Id { get; }
    public double[] Values { get; }
}

/// <summary>A tool-table entry returned by a controller or created locally.</summary>
public sealed class ToolTableEntry
{
    public ToolTableEntry(int id, int axisCount = 9)
    {
        Id = id;
        Offsets = new double[axisCount];
    }

    public int Id { get; }
    public double[] Offsets { get; }
    public double Radius { get; set; }
}

/// <summary>
/// Snapshot of the controller work parameters (coordinate systems, tool table,
/// tool-length offset, probe result). Immutable by convention: the owning service
/// replaces the whole snapshot with a fresh clone whenever data changes and
/// publishes it through <c>IGrblService.WorkParametersChanges</c>, so observers
/// never hold a reference that another thread mutates under them.
/// </summary>
public sealed class WorkParameters
{
    public static readonly string[] CoordinateSystemCodes =
        ["G54", "G55", "G56", "G57", "G58", "G59", "G59.1", "G59.2", "G59.3"];

    /// <summary>Coordinate systems the controller can actually select/write.
    /// Classic grbl 1.1 (and classic forks like mega5x 1.2i) support only G54..G59
    /// — G59.1/G59.2/G59.3 are grblHAL-only and classic firmware rejects them with
    /// error 29, so the UI must not offer them on non-HAL controllers.</summary>
    public static IReadOnlyList<string> SupportedSystems(bool isGrblHal) =>
        isGrblHal ? CoordinateSystemCodes : CoordinateSystemCodes[..6];

    public List<WorkCoordinateSystem> CoordinateSystems { get; } = new();
    public List<ToolTableEntry> Tools { get; } = new();
    public double[] ToolLengthOffset { get; set; } = new double[9];
    public double[]? ProbePosition { get; set; }
    public bool ProbeSuccess { get; set; }
    public double ToolLengthOffsetReference { get; set; } = double.NaN;
    public string ActiveCoordinateSystem { get; set; } = "G54";
    public int ActiveTool { get; set; }

    public static WorkParameters CreateDefault(int axisCount, int toolCount = 0)
    {
        axisCount = Math.Clamp(axisCount, 1, 9);
        var result = new WorkParameters();
        for (var i = 0; i < CoordinateSystemCodes.Length; i++)
            result.CoordinateSystems.Add(new WorkCoordinateSystem(CoordinateSystemCodes[i], i + 1, axisCount));
        for (var i = 0; i <= toolCount; i++)
            result.Tools.Add(new ToolTableEntry(i, axisCount));
        return result;
    }

    /// <summary>Parses the bracketed lines returned by classic grbl/grblHAL $#.</summary>
    public static WorkParameters Parse(IEnumerable<string> lines, int axisCount = 3, int toolCount = 0)
    {
        var result = new WorkParameters();
        result.ToolLengthOffset = new double[Math.Clamp(axisCount, 1, 9)];
        var input = lines.Select(line => line?.Trim() ?? string.Empty)
            .Where(line => line.Length > 0)
            .ToList();

        // Seed coordinate systems/tools from every reported code so a partial dump
        // (e.g. classic grbl split over multiple $#s) keeps them all visible.
        var codes = input.Select(GetCode).Where(code => code is not null).Cast<string>().Distinct().ToList();
        var sawTable = codes.Count > 0;
        if (!sawTable)
            result = CreateDefault(axisCount, toolCount);
        else
        {
            foreach (var code in codes)
                if (code is not "TLO" and not "PRB" and not "TOOL" and not "T")
                    UpsertCoordinateSystem(result, code, string.Empty);
        }

        foreach (var raw in input)
            result.ApplyLine(raw);
        return result;
    }

    /// <summary>Applies a single bracketed work-parameter line, e.g. <c>[G54:1,2,3]</c>.</summary>
    public bool ApplyLine(string raw)
    {
        var line = raw?.Trim();
        if (string.IsNullOrWhiteSpace(line) || line[0] != '[' || line[^1] != ']')
            return false;
        var colon = line.IndexOf(':');
        if (colon <= 1)
            return false;
        var code = line[1..colon];
        var values = line[(colon + 1)..^1].Split('|', StringSplitOptions.RemoveEmptyEntries);
        switch (code)
        {
            case "G28":
            case "G30":
            case "G54":
            case "G55":
            case "G56":
            case "G57":
            case "G58":
            case "G59":
            case "G59.1":
            case "G59.2":
            case "G59.3":
            case "G92":
                UpsertCoordinateSystem(this, code, values.FirstOrDefault() ?? string.Empty);
                return true;
            case "TLO":
                ToolLengthOffset = ParseValues(values.FirstOrDefault() ?? string.Empty, ToolLengthOffset.Length);
                return true;
            case "TLR":
                if (double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var reference))
                    ToolLengthOffsetReference = reference;
                return true;
            case "PRB":
                var probe = values.FirstOrDefault() ?? string.Empty;
                var probeParts = probe.Split(':');
                ProbePosition = ParseValues(probeParts[0], ToolLengthOffset.Length);
                ProbeSuccess = probeParts.Length < 2 || probeParts[1] == "1";
                return true;
            case "TOOL":
            case "T":
                if (int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    UpsertTool(this, id, values.Skip(1).FirstOrDefault() ?? string.Empty);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Deep copy so services can replace the snapshot without aliasing.</summary>
    public WorkParameters Clone()
    {
        var result = new WorkParameters
        {
            ToolLengthOffset = (double[])ToolLengthOffset.Clone(),
            ProbePosition = ProbePosition is null ? null : (double[])ProbePosition.Clone(),
            ProbeSuccess = ProbeSuccess,
            ActiveCoordinateSystem = ActiveCoordinateSystem,
            ActiveTool = ActiveTool,
            ToolLengthOffsetReference = ToolLengthOffsetReference,
        };
        foreach (var cs in CoordinateSystems)
        {
            var copy = new WorkCoordinateSystem(cs.Code, cs.Id, cs.Values.Length);
            Copy(cs.Values, copy.Values);
            result.CoordinateSystems.Add(copy);
        }
        foreach (var tool in Tools)
        {
            var copy = new ToolTableEntry(tool.Id, tool.Offsets.Length) { Radius = tool.Radius };
            Copy(tool.Offsets, copy.Offsets);
            result.Tools.Add(copy);
        }
        return result;
    }

    /// <summary>G10 L2 P number for a settable coordinate system (1..9), else 0.</summary>
    public static int CoordinateSystemNumber(string code) => code switch
    {
        "G54" => 1, "G55" => 2, "G56" => 3, "G57" => 4, "G58" => 5, "G59" => 6,
        "G59.1" => 7, "G59.2" => 8, "G59.3" => 9, _ => 0,
    };

    /// <summary>True when <paramref name="code"/> can be written with G10 L2 &lt;P&gt;.</summary>
    public static bool IsG10Writable(string code) => CoordinateSystemNumber(code) > 0;

    private static void UpsertCoordinateSystem(WorkParameters result, string code, string csv)
    {
        var item = result.CoordinateSystems.FirstOrDefault(x => x.Code == code);
        if (item is null)
        {
            item = new WorkCoordinateSystem(code, CoordinateSystemNumber(code), result.ToolLengthOffset.Length);
            result.CoordinateSystems.Add(item);
        }
        Copy(ParseValues(csv, item.Values.Length), item.Values);
    }

    private static void UpsertTool(WorkParameters result, int id, string csv)
    {
        var item = result.Tools.FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            item = new ToolTableEntry(id, result.ToolLengthOffset.Length);
            result.Tools.Add(item);
        }
        Copy(ParseValues(csv, item.Offsets.Length), item.Offsets);
    }

    private static double[] ParseValues(string csv, int count)
    {
        var result = new double[count];
        var values = csv.Split(',', StringSplitOptions.TrimEntries);
        for (var i = 0; i < result.Length && i < values.Length; i++)
            double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]);
        return result;
    }

    private static string? GetCode(string raw)
    {
        var line = raw?.Trim();
        if (string.IsNullOrWhiteSpace(line) || line[0] != '[')
            return null;
        var colon = line.IndexOf(':');
        return colon > 1 ? line[1..colon] : null;
    }

    private static void Copy(double[] source, double[] target) =>
        Array.Copy(source, target, Math.Min(source.Length, target.Length));
}