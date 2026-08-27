namespace CNC.Core.Services;

/// <summary>
/// Human-readable descriptions for grbl / grblHAL <c>error:&lt;n&gt;</c> replies.
/// Codes 0–38 are identical across classic grbl (1.1) and grblHAL (they share the
/// parser lineage); 39+ are grblHAL extensions. Used to turn a bare console line
/// like <c>error:79</c> into an actionable status-bar message.
/// </summary>
public static class GrblErrorCodes
{
    // Keyed by grbl status code. Entries are short so they fit the status bar.
    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [1] = "expected a command letter",
        [2] = "bad number format",
        [3] = "invalid statement",
        [4] = "negative value not allowed",
        [5] = "homing cycle is not enabled",
        [6] = "step pulse time too short",
        [7] = "EEPROM read failed — reset the controller",
        [8] = "'$' command ignored — invalid or not allowed now",
        [9] = "commands are locked out while in alarm or jog state",
        [10] = "soft limit exceeded",
        [11] = "line too long",
        [12] = "step rate too high",
        [13] = "safety door is open",
        [14] = "line exceeds EEPROM limit",
        [15] = "jog target exceeds machine travel",
        [16] = "invalid jog command — '$J=<axis> F<feed>' with no g-code",
        [17] = "laser mode requires PWM output",
        [20] = "unsupported or invalid g-code command",
        [21] = "conflicting g-code commands in the same modal group",
        [22] = "feed rate not set",
        [23] = "command requires an integer value",
        [24] = "conflicting axis-command usage in the block",
        [25] = "a g-code word was repeated",
        [26] = "no axis words found in the block",
        [27] = "invalid line number",
        [28] = "value word missing",
        [29] = "unsupported coordinate system",
        [30] = "G53 used with an invalid motion mode",
        [31] = "axis words not allowed with this command",
        [32] = "no axis words in the selected plane",
        [33] = "invalid target",
        [34] = "arc radius error",
        [35] = "no offsets in the selected plane",
        // grblHAL extensions
        [39] = "value out of range",
        [40] = "tool change pending",
        [41] = "spindle is not running",
        [42] = "illegal plane",
        [43] = "feed rate exceeds the maximum",
        [44] = "RPM out of range",
        [45] = "limit switch(es) engaged — motion is blocked",
        [46] = "homing required before motion",
        [50] = "e-stop is active — release it, then retry",
        [51] = "motor fault — check the drives",
        [79] = "critical alarm active (hard/soft limit, e-stop or motor fault) — release the cause, then Reset/Unlock again",
    };

    /// <summary>
    /// Human-readable description for an <c>error:&lt;code&gt;</c> reply, or
    /// <c>null</c> when the code isn't known. Prefixes nothing; callers format
    /// e.g. <c>error:79 — critical alarm active …</c>.
    /// </summary>
    public static string? Describe(int code) =>
        Descriptions.TryGetValue(code, out var text) ? text : null;
}
