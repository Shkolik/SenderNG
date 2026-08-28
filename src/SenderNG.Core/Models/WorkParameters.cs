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

/// <summary>Portable snapshot of the controller work parameters.</summary>
public sealed class WorkParameters
{
    public static readonly string[] CoordinateSystemCodes =
        ["G54", "G55", "G56", "G57", "G58", "G59", "G59.1", "G59.2", "G59.3"];

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
        foreach (var code in input.Select(GetCode).Where(code => code is not null).Cast<string>().Distinct())
            if (code is not "TLO" and not "PRB" and not "TOOL" and not "T")
                result.CoordinateSystems.Add(new WorkCoordinateSystem(code, CoordinateSystemNumber(code), result.ToolLengthOffset.Length));
        if (result.CoordinateSystems.Count == 0)
            result = CreateDefault(axisCount, toolCount);
        foreach (var raw in lines)
        {
            var line = raw?.Trim();
            if (string.IsNullOrWhiteSpace(line) || line[0] != '[' || line[^1] != ']')
                continue;
            var colon = line.IndexOf(':');
            if (colon <= 1)
                continue;
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
                    UpsertCoordinateSystem(result, code, values.FirstOrDefault() ?? string.Empty);
                    break;
                case "TLO":
                    result.ToolLengthOffset = ParseValues(values.FirstOrDefault() ?? string.Empty, result.ToolLengthOffset.Length);
                    break;
                case "PRB":
                    var probe = values.FirstOrDefault() ?? string.Empty;
                    var probeParts = probe.Split(':');
                    result.ProbePosition = ParseValues(probeParts[0], result.ToolLengthOffset.Length);
                    result.ProbeSuccess = probeParts.Length < 2 || probeParts[1] == "1";
                    break;
                case "TOOL":
                case "T":
                    if (int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        UpsertTool(result, id, values.Skip(1).FirstOrDefault() ?? string.Empty);
                    break;
            }
        }
        return result;
    }

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

    private static int CoordinateSystemNumber(string code) => code switch
    {
        "G54" => 1, "G55" => 2, "G56" => 3, "G57" => 4, "G58" => 5, "G59" => 6,
        "G59.1" => 7, "G59.2" => 8, "G59.3" => 9, _ => 0,
    };

    private static void Copy(double[] source, double[] target) =>
        Array.Copy(source, target, Math.Min(source.Length, target.Length));
}
