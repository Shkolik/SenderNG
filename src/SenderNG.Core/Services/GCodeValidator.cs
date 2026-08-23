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
        foreach (var raw in gcodeText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            n++;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("(") || trimmed.StartsWith(";") || trimmed.StartsWith("%"))
                continue;

            try
            {
                var block = trimmed;
                parser.ParseBlock(ref block, false);
            }
            catch (Exception ex)
            {
                // Any per-line parser failure is a validation error, never a crash:
                // the ported scanner can throw raw IndexOutOfRangeException on bad input.
                if (errors.Count < MaxErrors)
                    errors.Add($"Line {n}: {Describe(ex)}");
            }
        }
        return errors;
    }

    private static string Describe(Exception ex)
    {
        var msg = ex.Message;
        var cur = ex.InnerException;
        while (cur is not null)
        {
            if (!string.IsNullOrWhiteSpace(cur.Message))
                msg = string.IsNullOrWhiteSpace(msg) ? cur.Message : $"{msg} ({cur.Message})";
            cur = cur.InnerException;
        }
        return string.IsNullOrWhiteSpace(msg) ? "Invalid GCode" : msg;
    }
}
