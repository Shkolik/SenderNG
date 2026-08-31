
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

using System.Text.RegularExpressions;

namespace CNC.Core.Services;

/// <summary>
/// Dictionary of grbl / grblHAL <c>error:&lt;n&gt;</c> replies with the controller's
/// own explanation text (ioSender shows the same info; grblHAL exposes it via
/// <c>$I+</c>/<c>[ERRORCODE:]</c> and ioSender ships it in <c>error_codes_*.csv</c>).
/// Codes 0–38 are identical across classic grbl (1.1) and grblHAL — they share the
/// parser lineage — and 39+ are grblHAL extensions. Classic grbl 1.1 / the mega5x
/// 1.2i fork emit the same numbers for the shared range.
///
/// The descriptions below are the authoritative texts from grblHAL's
/// <c>errors.c</c> (Terje Io, GPLv3) and grbl 1.1's <c>report.h</c>. Keeping the
/// dictionary in code (rather than CSV files or a <c>$I+</c> round-trip) keeps the
/// core library cross-platform and dependency-free.
/// </summary>
public static class GrblErrorCodes
{
    /// <summary>Official controller explanation per error code.</summary>
    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [1] = "G-code words consist of a letter and a value. Letter was not found.",
        [2] = "Missing the expected G-code word value or numeric value format is not valid.",
        [3] = "'$' system command was not recognized or supported.",
        [4] = "Negative value received for an expected positive value.",
        [5] = "Homing cycle failure. Homing is not configured via settings.",
        [6] = "Step pulse time must be greater or equal to 2 microseconds.",
        [7] = "A settings read failed. Auto-restoring affected settings to default values.",
        [8] = "'$' command cannot be used unless controller state is IDLE.",
        [9] = "G-code commands are locked out during alarm or jog state.",
        [10] = "Soft limits cannot be enabled without homing also enabled.",
        [11] = "Max characters per line exceeded. Received command line was not executed.",
        [12] = "'$' setting value causes the step rate to exceed the maximum supported.",
        [13] = "Safety door detected as opened and door state initiated.",
        [14] = "Build info or startup line exceeded line length limit. Line not stored.",
        [15] = "Jog target exceeds machine travel. Jog command has been ignored.",
        [16] = "Jog command has no '=' or contains prohibited g-code.",
        [17] = "Laser mode requires PWM output.",
        [18] = "Reset asserted.", // grblHAL
        [19] = "Non-positive value.", // grblHAL
        [20] = "Unsupported or invalid g-code command found in block.",
        [21] = "More than one g-code command from same modal group found in block.",
        [22] = "Feed rate has not yet been set or is undefined.",
        [23] = "G-code command in block requires an integer value.",
        [24] = "More than one g-code command that requires axis words found in block.",
        [25] = "Repeated g-code word found in block.",
        [26] = "No axis words found in block for g-code command or current modal state which requires them.",
        [27] = "Line number value is invalid.",
        [28] = "G-code command is missing a required value word.",
        [29] = "G59.x work coordinate systems are not supported.",
        [30] = "G53 only allowed with G0 and G1 motion modes.",
        [31] = "Axis words found in block when no command or current modal state uses them.",
        [32] = "G2 and G3 arcs require at least one in-plane axis word.",
        [33] = "Motion command target is invalid.",
        [34] = "Arc radius value is invalid.",
        [35] = "G2 and G3 arcs require at least one in-plane offset word.",
        [36] = "Unused value words found in block.",
        [37] = "G43.1 dynamic tool length offset is not assigned to configured tool length axis.",
        [38] = "Tool number greater than max supported value or undefined tool selected.",
        [39] = "Value out of range.",
        [40] = "G-code command not allowed when tool change is pending.",
        [41] = "Spindle not running when motion commanded in CSS or spindle sync mode.",
        [42] = "Plane must be ZX for threading.",
        [43] = "Max. feed rate exceeded.",
        [44] = "RPM out of range.",
        [45] = "Only homing is allowed when a limit switch is engaged.",
        [46] = "Home machine to continue.",
        [47] = "ATC: current tool is not set. Set current tool with M61.",
        [48] = "Value word conflict.",
        [49] = "Power on self test failed. A hard reset is required.",
        [50] = "Emergency stop active.",
        [51] = "Motor fault.",
        [52] = "Setting value is out of range.",
        [53] = "Setting is not available, possibly due to limited driver support.",
        [54] = "Retract position is less than drill depth.",
        [55] = "Attempt to home two auto squared axes at the same time.",
        [57] = "Unexpected file demarcation.",
        [58] = "Port is not available.",
        [60] = "SD card mount error.",
        [61] = "File read error.",
        [62] = "Failed to open directory.",
        [63] = "Directory not found.",
        [64] = "SD card not mounted.",
        [65] = "File system not mounted.",
        [66] = "File system is read only.",
        [70] = "Bluetooth init error.",
        [71] = "Unknown operation found in expression.",
        [72] = "Divide by zero in expression attempted.",
        [73] = "Too large or too small argument provided.",
        [74] = "Argument is not valid for the operation.",
        [75] = "Expression is not valid.",
        [76] = "Either NaN (not a number) or infinity was returned from expression.",
        [77] = "Authentication required.",
        [78] = "Access denied.",
        [79] = "Not allowed while critical event is active.",
        [80] = "Flow statement only allowed in filesystem macro.",
        [81] = "Unknown flow statement.",
        [82] = "Stack overflow while executing flow statement.",
        [83] = "Out of memory while executing flow statement.",
        [84] = "Could not open file.",
        [85] = "File system format failed.",
        [86] = "Port is not usable.",
        [87] = "Tool in spindle.",
        [88] = "No tool in spindle.",
        [89] = "File delete failed.",
        [90] = "Cutter compensation is already active.",
        [91] = "Command not allowed while cutter compensation is active.",
        [92] = "Cutter compensation is not valid for current move.",
        [253] = "User defined error occurred.",
    };

    /// <summary>Actionable guidance appended to the status bar (not the console) for
    /// errors whose official text doesn't say what to do next.</summary>
    private static readonly Dictionary<int, string> Hints = new()
    {
        [15] = "If the machine is not homed and soft limits are enabled, run Home ($H) first.",
        [79] = "A critical alarm (hard/soft limit, e-stop or motor fault) is active — release the cause, then Reset and Unlock again.",
    };

    /// <summary>Matches the <c>error:&lt;n&gt;</c> reply prefix (public so localization can reuse it).</summary>
    public static readonly Regex ErrorReply = new("^error:([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Official controller explanation for an error code, or <c>null</c> when unknown.</summary>
    public static string? Describe(int code) =>
        Descriptions.TryGetValue(code, out var text) ? text : null;

    /// <summary>Optional actionable hint for the status bar, or <c>null</c>.</summary>
    public static string? Hint(int code) =>
        Hints.TryGetValue(code, out var text) ? text : null;

    /// <summary>
    /// Render a raw <c>error:&lt;n&gt;</c> console line with its explanation appended,
    /// e.g. <c>error:2 - Missing the expected G-code word value or numeric value
    /// format is not valid.</c> (ioSender format). Unknown codes pass through
    /// unchanged; non-error lines pass through unchanged.
    /// </summary>
    public static string Decorate(string line)
    {
        var match = ErrorReply.Match(line);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var code))
            return line;
        var description = Describe(code);
        return description is null ? line : $"{line} - {description}";
    }
}
