
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

using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class ControllerInfoParserTests
{
    [Fact]
    public void Classic_grbl_identity_sets_version()
    {
        var caps = ControllerInfoParser.ParseInfo(["Grbl 1.1h ['$' for help]"]);
        Assert.True(caps.IsKnown);
        Assert.False(caps.IsGrblHAL);
        Assert.Equal("1.1h", caps.Version);
        // classic grbl 1.1 has no M7 signal -> mist hidden
        Assert.False(caps.CoolantMist);
        Assert.True(caps.CoolantFlood);
        Assert.Equal(3, caps.NumAxes);
    }

    [Fact]
    public void GrblHAL_ver_line_sets_version_build_and_firmware()
    {
        var caps = ControllerInfoParser.ParseInfo(["[VER:1.1f.20230820:grblHAL]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.Equal("1.1f", caps.Version);
        Assert.Equal(20230820, caps.Build);
        Assert.Equal("grblHAL", caps.Firmware);
        // grblHAL supports mist
        Assert.True(caps.CoolantMist);
    }

    [Fact]
    public void Mega5x_1_2i_reply_sets_axes_and_tools_but_is_not_grblHAL()
    {
        // Captured from a real mega5x board (grbl 1.2i, COM7): axes come from
        // [AXS:], the 4th [OPT:] field is the tool count, not axes. The 1.2i fork
        // is classic grbl — it emits [VER:]/[AXS:]/[OPT:] but no FIRMWARE:grblHAL,
        // so it must NOT be classified as grblHAL (regression: app used to "mix
        // 1.2i with HAL").
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.2i.20260521:]", "[AXS:5:XYZAB]", "[OPT:VNMGH,35,255,48]"]);
        Assert.True(caps.IsKnown);
        Assert.False(caps.IsGrblHAL);
        Assert.Equal("1.2i", caps.Version);
        Assert.Equal(20260521, caps.Build);
        Assert.Equal("grbl", caps.Firmware);
        Assert.Equal(5, caps.NumAxes);
        Assert.Equal(35, caps.PlanBufferSize);
        Assert.Equal(255, caps.SerialBufferSize);
        Assert.Equal(48, caps.NumTools);
        Assert.True(caps.CoolantMist); // 'M' option present (not HAL)
        Assert.True(caps.HomingEnabled); // 'H' option present
    }

    [Fact]
    public void R_P2040_grblHAL_reply_parses_5_field_opt_layout()
    {
        // Captured from a real RP2040 grblHAL board (COM9): [OPT:] has 5 fields
        // (letters, plan, serial, AXES, tools) — the 4th field is axes, not tools.
        var caps = ControllerInfoParser.ParseInfo(
            ["GrblHAL 1.1f ['$' or '$HELP' for help]", "[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,32]", "[AXS:4:XYZA]", "[FIRMWARE:grblHAL]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.Equal("1.1f", caps.Version);
        Assert.Equal(20260827, caps.Build);
        Assert.Equal("grblHAL", caps.Firmware);
        Assert.Equal(4, caps.NumAxes);
        Assert.Equal(100, caps.PlanBufferSize);
        Assert.Equal(1024, caps.SerialBufferSize);
        Assert.Equal(32, caps.NumTools);
        Assert.True(caps.CoolantMist); // 'M' option present
    }

    [Fact]
    public void Classic_grbl_1_1_I_reply_is_not_grblHAL()
    {
        // Classic grbl 1.1 $I shape: [VER:]/[OPT:]/[FIRMWARE:grbl]/[FW_BUILD:...].
        // [FIRMWARE:grbl] must NOT flag HAL (the old default-branch bug did).
        var caps = ControllerInfoParser.ParseInfo(
            ["Grbl 1.1h ['$' for help]", "[VER:1.1h.20170828:]", "[OPT:V,15,128]", "[FIRMWARE:grbl]", "[FW_BUILD:20170828]"]);
        Assert.True(caps.IsKnown);
        Assert.False(caps.IsGrblHAL);
        Assert.Equal("1.1h", caps.Version);
        Assert.Equal("grbl", caps.Firmware);
        Assert.Equal(3, caps.NumAxes);
        Assert.False(caps.CoolantMist); // no 'M' option
    }

    [Fact]
    public void Real_grblHAL_reply_is_flagged_hal_via_firmware_line()
    {
        // Real grblHAL $I shape: banner + [VER:]/[OPT:]/[FIRMWARE:grblHAL]/[FW_BUILD:...].
        // Only the FIRMWARE:grblHAL marker (and the banner) classify it as HAL.
        var caps = ControllerInfoParser.ParseInfo(
            ["grblHAL 1.1h ['$' for help]", "[VER:1.1h.20240101:]", "[OPT:VNM,16,128]", "[FIRMWARE:grblHAL]", "[FW_BUILD:20240101]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.Equal("1.1h", caps.Version);
        Assert.Equal(20240101, caps.Build);
        Assert.Equal("grblHAL", caps.Firmware);
        Assert.Equal(3, caps.NumAxes);
        Assert.True(caps.CoolantMist); // grblHAL
    }

    [Fact]
    public void Opt_line_sets_buffers_and_tools_but_not_axes()
    {
        // Without an [AXS:] line, OPT's numbers are plan/serial/tools — axes stay 3.
        var caps = ControllerInfoParser.ParseInfo(["[OPT:Z+FRPNM-SV,64,256,8]"]);
        Assert.True(caps.IsKnown);
        Assert.Equal(64, caps.PlanBufferSize);
        Assert.Equal(256, caps.SerialBufferSize);
        Assert.Equal(8, caps.NumTools);
        Assert.Equal(3, caps.NumAxes);
        Assert.True(caps.CoolantMist); // 'M' present
        Assert.True(caps.CoolantFlood);
    }

    [Fact]
    public void Full_set_combining_ver_axs_opt_is_processed()
    {
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.2i.20210101:grblHAL]", "[AXS:5:XYZAB]", "[OPT:Z+FRPNM-SV,128,256,4]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.Equal("1.2i", caps.Version);
        Assert.Equal(5, caps.NumAxes);
        Assert.Equal(4, caps.NumTools);
    }

    [Fact]
    public void Unrecognized_lines_returns_unknown()
    {
        var caps = ControllerInfoParser.ParseInfo(["garbage", "", "$$..."]);
        Assert.False(caps.IsKnown);
    }

    [Fact]
    public void GrblHAL_signals_line_reports_optional_signals()
    {
        // grblHAL $I lists built-in signals one letter per char in SignalLetters
        // order (ioSender maps each to the matching Signals flag).
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:grblHAL]", "[SIGNALS:XYZABCUERDHSOF]"]);
        Assert.True(caps.IsKnown);
        Assert.True(caps.IsGrblHAL);
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.SafetyDoor) != 0); // D
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.EStop) != 0);      // E
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.MotorFault) != 0); // F
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.LimitX) != 0);     // X
        Assert.False((caps.OptionalSignals & CNC.Core.Signals.MotorWarning) != 0); // M absent
    }

    [Fact]
    public void Classic_grbl_opt_plus_reports_safety_door()
    {
        // Classic grbl reports the safety door via a '+' in the first OPT field
        // (ioSender: `if (s[0].Contains('+')) OptionalSignals |= SafetyDoor`).
        var caps = ControllerInfoParser.ParseInfo(["[OPT:Z+FRPNM-SV,64,256,8]"]);
        Assert.True(caps.IsKnown);
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.SafetyDoor) != 0);
        Assert.False((caps.OptionalSignals & CNC.Core.Signals.EStop) != 0);
    }

    [Fact]
    public void Classic_grbl_without_plus_reports_no_optional_signals()
    {
        // The mega5x 1.2i / classic grbl 1.1 $I shape (no [SIGNALS:], no '+' in
        // OPT) discovers no optional signals — the strip shows only axis limits
        // plus the always-on H/S/R/P, exactly as ioSender behaves on these boards.
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.2i.20260521:]", "[AXS:5:XYZAB]", "[OPT:VNMGH,35,255,48]"]);
        Assert.True(caps.IsKnown);
        Assert.Equal(CNC.Core.Signals.Off, caps.OptionalSignals);
    }

    [Fact]
    public void GrblHAL_newopt_reports_block_delete_estop_motor()
    {
        // grblHAL [NEWOPT:] tokens map to optional signals (ioSender semantics).
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:grblHAL]", "[NEWOPT:BD,ES,MW,OS]"]);
        Assert.True(caps.IsKnown);
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.BlockDelete) != 0);
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.EStop) != 0);
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.MotorWarning) != 0);
        Assert.True((caps.OptionalSignals & CNC.Core.Signals.OptionalStop) != 0);
    }
}

public class ControllerCapabilitiesDemoAndProxyTests
{
    [Fact]
    public void DemoGrblService_reports_full_capabilities()
    {
        using var demo = new DemoGrblService(new MachineProfile());
        Assert.True(demo.Capabilities.IsKnown);
        Assert.True(demo.Capabilities.CoolantMist);
        Assert.True(demo.Capabilities.CoolantFlood);
    }

    [Fact]
    public void GrblServiceProxy_forwards_capabilities_from_inner()
    {
        var inner = new DemoGrblService(new MachineProfile());
        var proxy = new GrblServiceProxy(inner);
        Assert.Same(inner.Capabilities, proxy.Capabilities);
    }

    [Fact]
    public void Controller_key_fingerprints_firmware_and_options()
    {
        // The key scopes editor-local metadata (tool names) per machine: the $I
        // firmware identity + raw OPT/AXS. Different firmware (version/build/
        // options) must produce a different key.
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,32]", "[AXS:4:XYZA]"]);

        Assert.Equal("grbl|1.1f.20260827|VNMSL+,100,1024,4,32|4:XYZA", caps.ControllerKey);

        // Deterministic: re-parsing the same $I dump yields the identical key
        // (the key takes no endpoint — by construction it cannot change when the
        // machine moves to another COM port or IP, so names follow the machine).
        var reparsed = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,32]", "[AXS:4:XYZA]"]);
        Assert.Equal(caps.ControllerKey, reparsed.ControllerKey);

        // A different machine (different firmware options): different key.
        var otherBoard = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,48]", "[AXS:4:XYZA]"]);
        Assert.NotEqual(caps.ControllerKey, otherBoard.ControllerKey);
    }

    [Fact]
    public void Controller_key_uses_the_exact_axis_letter_set()
    {
        // A 4-axis mill (XYZA) and a 4-axis lathe (XYZC) with identical firmware
        // and options still get distinct keys because [AXS:] letters are kept raw.
        var mill = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,32]", "[AXS:4:XYZA]"]);
        var lathe = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,32]", "[AXS:4:XYZC]"]);
        Assert.NotEqual(mill.ControllerKey, lathe.ControllerKey);
    }

    [Fact]
    public void Controller_key_classic_grbl_unknown_and_clones()
    {
        // Classic grbl 1.1h: no [AXS:] line -> raw axes empty; key still carries
        // the version.build and OPT numbers that distinguish boards.
        var classic = ControllerInfoParser.ParseInfo(
            ["Grbl 1.1h ['$' for help]", "[VER:1.1h.20170828:]", "[OPT:V,15,128]"]);
        Assert.Equal("grbl|1.1h.20170828|V,15,128|", classic.ControllerKey);

        // An unidentified controller degenerates to the shared "unknown" key
        // (no tool table is shown then, so names can't be saved under it).
        Assert.Equal("unknown", ControllerCapabilities.Unknown.ControllerKey);

        // Two BYTE-IDENTICAL boards (clones — same firmware build, options, axis
        // letters): same key by design — the tooling you set up on one clone
        // applies to the other.
        var cloneA = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,32]", "[AXS:4:XYZA]"]);
        var cloneB = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[OPT:VNMSL+,100,1024,4,32]", "[AXS:4:XYZA]"]);
        Assert.Equal(cloneA.ControllerKey, cloneB.ControllerKey);
    }

    [Fact]
    public void Default_interface_member_is_unknown()
    {
        // A minimal double that doesn't override Capabilities falls back to Unknown.
        // (Default interface members are only reachable through the interface type.)
        var fake = new BareFake();
        var iface = (CNC.Core.Abstractions.IGrblService)fake;
        Assert.False(iface.Capabilities.IsKnown);
    }

    private sealed class BareFake : CNC.Core.Abstractions.IGrblService
    {
        public bool IsConnected => false;
        public System.IObservable<MachineState> State { get; } =
            new System.Reactive.Subjects.BehaviorSubject<MachineState>(new MachineState(3));
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }
}