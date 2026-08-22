using CNC.GCode;

namespace CNC.Core.Services;

/// <summary>
/// Validates G-code text line by line using <see cref="GCodeParser"/>,
/// collecting errors as "Line {n}: {message}" entries (empty list = valid).
/// </summary>
public static class GCodeValidator
{
    private const int MaxErrors = 10;

    public static IReadOnlyList<string> Validate(string gcodeText)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(gcodeText))
            return errors;

        var parser = new GCodeParser();
        var n = 0;
        foreach (var raw in gcodeText.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            n++;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("(") || trimmed.StartsWith(";") || trimmed.StartsWith("%"))
                continue;

            try
            {
                var block = raw;
                parser.ParseBlock(ref block, false);
            }
            catch (GCodeException ex)
            {
                if (errors.Count < MaxErrors)
                    errors.Add($"Line {n}: {ex.Message}");
            }
        }
        return errors;
    }
}
