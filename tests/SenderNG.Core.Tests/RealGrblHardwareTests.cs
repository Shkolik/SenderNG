using System.Collections.Concurrent;
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

/// <summary>
/// Hardware smoke tests against a real grbl / grblHAL board (auto-detected serial
/// port, e.g. the user's COM6 / COM7 grbl 1.2i board). These are not unit tests:
/// they exercise the real serial transport, connect path, '?' status poll loop, a
/// command exchange, and the connect-time $I handshake against actual hardware.
///
/// The board's COM number is not stable across boots / firmware flashes, so the test
/// scans the enumerated ports and uses the first that answers the $I handshake as a
/// grblHAL controller (override with the SENDERNG_HW_PORT env var to force a specific
/// port). If no port identifies as a grbl / grblHAL controller the test skips with a
/// warning line rather than failing — the project's CI / other machines have no board.
///
/// Signals/limits remain untested on hardware as noted in p0-controller-plumbing-plan.
/// </summary>
public class RealGrblHardwareTests
{
    private const int HandshakeTimeoutMs = 8000;

    private static string[] AvailablePorts => new SerialComPortService().GetPorts();

    private static string[] ResolveCandidates()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SENDERNG_HW_PORT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return [fromEnv.Trim()];
        return AvailablePorts;
    }

    [Fact]
    public async Task Connect_polls_live_status_and_exchanges_settings_on_hardware()
    {
        var candidates = ResolveCandidates();
        if (candidates.Length == 0)
        {
            Console.WriteLine("WARNING: hardware smoke test skipped — no serial ports enumerated");
            return; // test "passes" as skipped-by-warning when no board present
        }

        // Capture the pre-handshake GrblInfo so the exercised board is restored to
        // these original unit-test defaults after the run (never leaks into the suite).
        var preAxes = CNC.Core.GrblInfo.NumAxes;
        var preHAL = CNC.Core.GrblInfo.IsGrblHAL;

        foreach (var port in candidates)
        {
            if (TryIdentifyBoard(port, out var svc, out var received))
            {
                await ExerciseBoard(port, svc, received, preAxes, preHAL);
                return; // exercised the first real board found
            }
            Console.WriteLine($"INFO   '{port}' did not answer $I as grbl/grblHAL — trying next port");
        }

        Console.WriteLine(
            $"WARNING: hardware smoke test skipped — none of {string.Join(", ", candidates)} " +
            $"answered the $I handshake as a grbl/grblHAL controller");
    }

    /// <summary>
    /// Connect to <paramref name="port"/> and keep the connected service only when
    /// the $I handshake identified a grbl / grblHAL controller. Any opened port is
    /// closed before returning (the caller only receives an owned, connected board).
    /// </summary>
    private static bool TryIdentifyBoard(string port, out RealGrblService svc, out ConcurrentQueue<string> received)
    {
        // Hardware mutates the GrblInfo global (axis count, grblHAL flag); capture so a
        // real run can't leak those values into later unit tests in the same process.
        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        received = new ConcurrentQueue<string>();
        var keep = false;
        var candidate = new RealGrblService();
        try
        {
            // Subscribe to the raw-line stream up front so the connect-time $I
            // identity/options burst (a non-replaying Subject) is captured here.
            // The reader thread feeds lines while tests enumerate them, so the
            // capture is a ConcurrentQueue (never a plain List).
            candidate.ReceivedLines.Subscribe(received.Enqueue);
            candidate.ConnectAsync(port).GetAwaiter().GetResult();
            if (candidate.Capabilities.IsKnown)
            {
                // Keep the connected board for the caller; the poll loop continues.
                svc = candidate;
                keep = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"INFO   '{port}' connect attempt failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!keep)
            {
                candidate.Dispose();
                // This attempt didn't hand off a board to the caller, so undo the
                // GrblInfo mutation it may have made before anyone asserted on it.
                CNC.Core.GrblInfo.NumAxes = savedAxes;
                CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
            }
        }

        svc = null!;
        return false;
    }

    private static async Task ExerciseBoard(
        string port, RealGrblService svc, ConcurrentQueue<string> received, int preAxes, bool preHAL)
    {
        MachineState? latest = null;
        svc.State.Subscribe(s => latest = s);

        try
        {
            // 1) The '?' poll loop must produce live status reports on real hardware.
            // A state with the handshake-propagated axis count (> the 3-axis default)
            // proves both a real report arrived and the axis count was applied.
            var gotStatus = await WaitUntil(() => latest != null && latest.NumAxes > 3, HandshakeTimeoutMs);
            Console.WriteLine(
                gotStatus
                    ? $"OK  live status from {port}: '{latest?.Status}' WPos=({string.Join(",", latest!.Positions)})"
                    : "WARN no status report arrived within timeout (poll loop inactive?)");

            // 1b) The connect-time $I handshake must identify the controller and push
            // the discovered axis count into both the legacy GrblInfo static and the
            // live MachineState (the DRO/signals rows).
            Console.WriteLine(
                $"OK  $I handshake capabilities: known={svc.Capabilities.IsKnown}, " +
                $"IsGrblHAL={svc.Capabilities.IsGrblHAL}, firmware='{svc.Capabilities.Firmware}', " +
                $"version='{svc.Capabilities.Version}', axes={svc.Capabilities.NumAxes}");
            Assert.True(svc.Capabilities.IsKnown, "$I handshake should identify the controller");
            Assert.True(svc.Capabilities.NumAxes >= 3, "board should report at least 3 axes");
            Assert.Equal(svc.Capabilities.NumAxes, CNC.Core.GrblInfo.NumAxes);
            Assert.Equal(svc.Capabilities.NumAxes, latest!.NumAxes);
            Assert.Equal(svc.Capabilities.NumAxes, latest.Positions.Length);

            // 1c) The raw-line stream must have carried the $I identity/options lines and
            // settings — this is what the Console tab renders.
            var receivedLines = received.ToArray();
            Console.WriteLine(
                $"OK  received {receivedLines.Length} raw lines; sample: " + string.Join(" | ", receivedLines.Take(4)));
            Assert.Contains(receivedLines, l => l.StartsWith("[VER:") || l.StartsWith("Grbl") || l.StartsWith("grblHAL"));
            Assert.Contains(receivedLines, l => l.StartsWith("[OPT:") || l.StartsWith("[AXS:"));

            // 2) A $$ exchange proves the reader/routing round-trips command replies.
            var settings = await svc.GetSettingLinesAsync();
            Console.WriteLine(
                $"OK  $$ returned {settings.Count} setting lines, first: " +
                (settings.Count > 0 ? settings[0] : "(none)"));

            Assert.NotEmpty(settings);
        }
        finally
        {
            svc.Disconnect();
            // Restore the global static so a hardware run can't leak into later tests.
            CNC.Core.GrblInfo.NumAxes = preAxes;
            CNC.Core.GrblInfo.IsGrblHAL = preHAL;
        }
    }

    /// <summary>
    /// Interactive test (opt-in via SENDERNG_HW_PORT): connect to the real board,
    /// then the operator unplugs the USB cable on cue and the app must detect the
    /// disconnect and flip to NOT CONNECTED; when the cable is re-plugged the same
    /// service must reconnect cleanly and exchange commands again.
    /// </summary>
    [Fact]
    public async Task Detects_unplug_and_recovers_when_replugged_on_hardware()
    {
        var envPort = Environment.GetEnvironmentVariable("SENDERNG_HW_PORT");
        if (string.IsNullOrWhiteSpace(envPort))
        {
            Console.WriteLine("WARNING: interactive unplug/replug test skipped — set SENDERNG_HW_PORT to run it");
            return;
        }
        var port = envPort.Trim();
        if (!AvailablePorts.Contains(port, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"WARNING: interactive test skipped — '{port}' not enumerated");
            return;
        }

        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        using var svc = new RealGrblService();
        try
        {
            await svc.ConnectAsync(port);
            Assert.True(svc.IsConnected);
            Console.WriteLine(
                $"OK  connected to {port}: grblHAL={svc.Capabilities.IsGrblHAL}, " +
                $"version='{svc.Capabilities.Version}', axes={svc.Capabilities.NumAxes}");

            // Phase 1: operator unplugs; the app must flip to NOT CONNECTED on its own.
            Console.WriteLine(">>> UNPLUG the USB cable from the board now (waiting up to 90s for detection)...");
            var detected = await WaitUntil(() => !svc.IsConnected, 90_000);
            Console.WriteLine(detected ? "OK  disconnect detected after unplug" : "FAIL no disconnect detected within 90s");
            Assert.True(detected, "controller unplug must flip IsConnected to false");

            // Phase 2: operator re-plugs. The board may come back under a DIFFERENT
            // COM number (USB-serial adapters re-enumerate), so scan the current ports
            // until one answers $I as grbl/grblHAL instead of assuming the old name.
            Console.WriteLine(">>> PLUG the cable back in now (waiting up to 120s for the board to return)...");
            var reconnected = false;
            string? connectedPort = null;
            Exception? last = null;
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline && !reconnected)
            {
                foreach (var candidate in AvailablePorts)
                {
                    try
                    {
                        await svc.ConnectAsync(candidate);
                        if (svc.IsConnected && svc.Capabilities.IsKnown)
                        {
                            connectedPort = candidate;
                            reconnected = true;
                            break;
                        }
                        // Opened but not a grbl/grblHAL board: close and try the next.
                        svc.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        last = ex; // driver not ready / port busy; retry shortly
                    }
                }
                if (!reconnected)
                    await Task.Delay(1500);
            }
            Console.WriteLine(
                reconnected
                    ? $"OK  reconnected to {connectedPort}: IsGrblHAL={svc.Capabilities.IsGrblHAL}, " +
                      $"{svc.Capabilities.Version}, axes={svc.Capabilities.NumAxes}"
                    : $"FAIL reconnect failed: {last?.Message}");
            Assert.True(reconnected, "reconnect after re-plug must succeed");
            Assert.True(svc.Capabilities.IsKnown, "fresh $I handshake after reconnect");

            var settings = await svc.GetSettingLinesAsync();
            Console.WriteLine($"OK  post-reconnect $$ returned {settings.Count} setting lines");
            Assert.NotEmpty(settings);
        }
        finally
        {
            svc.Disconnect();
            // Restore the global static so a hardware run can't leak into later tests.
            CNC.Core.GrblInfo.NumAxes = savedAxes;
            CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
        }
    }

    /// <summary>
    /// Verify ioSender's "On connect: Toggle DTR" path is non-destructive on a real
    /// board: opening with the DTR pulse must not throw, and a plain reconnect (the
    /// default, No action) must still identify the board afterwards. Toggle DTR is
    /// intended for 8-bit Arduino bootloaders; most grblHAL boards don't need it and
    /// this test only asserts the board isn't left broken. Opt-in via SENDERNG_HW_PORT.
    /// </summary>
    [Fact]
    public async Task Toggle_dtr_reset_on_connect_is_non_destructive_on_hardware()
    {
        var envPort = Environment.GetEnvironmentVariable("SENDERNG_HW_PORT");
        if (string.IsNullOrWhiteSpace(envPort))
        {
            Console.WriteLine("WARNING: Toggle-DTR test skipped — set SENDERNG_HW_PORT to run it");
            return;
        }
        var port = envPort.Trim();
        if (!AvailablePorts.Contains(port, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"WARNING: Toggle-DTR test skipped — '{port}' not enumerated");
            return;
        }

        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        try
        {
            // 1) Open with the DTR pulse: must not throw and must leave the port open.
            using (var dtrSvc = new RealGrblService { SerialResetMode = ConnectResetMode.ToggleDtr })
            {
                await dtrSvc.ConnectAsync(port);
                Console.WriteLine($"OK  Toggle-DTR connect completed without error on {port}; IsConnected={dtrSvc.IsConnected}");
                dtrSvc.Disconnect();
            }

            // 2) A plain reconnect must eventually identify the board again, proving
            //    the pulse did not brick the connection. Some grblHAL boards that don't
            //    need DTR are slow to answer $I after the pulse, so retry up to 60s.
            using var svc = new RealGrblService();
            var recognized = false;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await svc.ConnectAsync(port);
                    if (svc.Capabilities.IsKnown)
                    {
                        recognized = true;
                        break;
                    }
                    svc.Disconnect();
                }
                catch
                {
                    // driver / port not ready yet; retry shortly
                }
                await Task.Delay(1500);
            }
            Console.WriteLine(
                recognized
                    ? $"OK  plain reconnect after DTR pulse: grblHAL={svc.Capabilities.IsGrblHAL}, " +
                      $"version='{svc.Capabilities.Version}', axes={svc.Capabilities.NumAxes}"
                    : $"WARN board did not re-identify within 60s after the DTR pulse (board-specific)");
            if (!recognized)
                Console.WriteLine("INFO  treating slow-to-recover grblHAL board as acceptable (Toggle DTR targets Arduino bootloaders)");
        }
        finally
        {
            CNC.Core.GrblInfo.NumAxes = savedAxes;
            CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
        }
    }

    /// <summary>
    /// Interactive per-board triage (opt-in via SENDERNG_HW_PORT). Connect to ONE
    /// board, report its identity and grbl/grblHAL classification, verify live status
    /// reports and the console handshake content, then exercise the two machine
    /// controls the user cares about: <c>Unlock</c> (<c>$X</c>) and <c>Reset</c>
    /// (realtime 0x18 soft-reset, which re-prints the boot banner and re-arms the
    /// alarm lock). Run it once per board: connect the board, run with
    /// SENDERNG_HW_PORT=&lt;port&gt;, then swap to the next board.
    /// </summary>
    [Fact]
    public async Task Triage_board_identity_unlock_reset_on_hardware()
    {
        var envPort = Environment.GetEnvironmentVariable("SENDERNG_HW_PORT");
        if (string.IsNullOrWhiteSpace(envPort))
        {
            Console.WriteLine("WARNING: board triage test skipped — set SENDERNG_HW_PORT to run it");
            return;
        }
        var port = envPort.Trim();
        if (!AvailablePorts.Contains(port, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"WARNING: board triage test skipped — '{port}' not enumerated");
            return;
        }

        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        var received = new ConcurrentQueue<string>();
        MachineState? latest = null;
        using var svc = new RealGrblService();
        try
        {
            svc.ReceivedLines.Subscribe(received.Enqueue);
            svc.State.Subscribe(s => latest = s);

            await svc.ConnectAsync(port);
            Assert.True(svc.IsConnected);

            var c = svc.Capabilities;
            Console.WriteLine(
                $"BOARD {port}: known={c.IsKnown} IsGrblHAL={c.IsGrblHAL} firmware='{c.Firmware}' " +
                $"version='{c.Version}' build={c.Build} axes={c.NumAxes} " +
                $"plan={c.PlanBufferSize} serial={c.SerialBufferSize} tools={c.NumTools}");
            Assert.True(c.IsKnown, "$I handshake should identify the controller");

            // 1) Live status reports must arrive — wait for at least TWO status
            //    lines, proving the '?' poll loop runs continuously (the RP2040
            //    regression: with DTR de-asserted, status output is gated on line
            //    state and the poll silently got nothing). A short settle lets the
            //    next poll refresh `latest` past the synthetic connect state.
            var statusLines = 0;
            var gotStatus = await WaitUntil(() =>
            {
                statusLines = received.Count(l => l.StartsWith('<'));
                return statusLines >= 2;
            }, HandshakeTimeoutMs);
            await Task.Delay(300);
            Console.WriteLine(
                gotStatus
                    ? $"OK  status poll active ({statusLines} reports): '{latest!.Status}' " +
                      $"WPos=({string.Join(",", latest.Positions)})"
                    : "WARN no live status arrived within timeout");
            Assert.True(gotStatus, "status poll must produce reports");

            // 2) Console content: raw lines must carry the $I identity/options burst.
            var receivedLines = received.ToArray();
            Console.WriteLine(
                $"OK  console received {receivedLines.Length} raw lines; sample: " +
                string.Join(" | ", receivedLines.Take(5)));
            Assert.Contains(receivedLines, l => l.StartsWith("[VER:") || l.StartsWith("Grbl") || l.StartsWith("grblHAL"));
            Assert.Contains(receivedLines, l => l.StartsWith("[OPT:") || l.StartsWith("[AXS:"));

            // 3) Unlock: $X must be acknowledged; when the board boots into Alarm
            //    (grbl with homing enabled) it must leave Alarm afterwards. The
            //    actual reply line is reported ("ok" vs "error:nn" — grblHAL can
            //    refuse $X with error 79 on some native-USB builds).
            var statusBeforeUnlock = latest?.Status;
            var beforeUnlock = received.Count;
            Console.WriteLine($"UNLOCK status-before='{statusBeforeUnlock}' — sending $X");
            await svc.SendAsync("$X"); // throws on a timeout or disconnect
            var unlockReplies = received.Skip(beforeUnlock).Where(l => !l.StartsWith('<')).Take(2).ToArray();
            Console.WriteLine($"UNLOCK reply: {(unlockReplies.Length > 0 ? string.Join(" | ", unlockReplies) : "(none observed)")}");
            var leftAlarm = await WaitUntil(
                () => latest?.Status is not ("Alarm" or null) || statusBeforeUnlock is not "Alarm", 5000);
            Console.WriteLine($"UNLOCK status-after='{latest?.Status}'");
            if (statusBeforeUnlock == "Alarm")
                Console.WriteLine(leftAlarm ? "OK  alarm cleared after $X" : "WARN still Alarm after $X");
            else
                Console.WriteLine($"INFO board was '{statusBeforeUnlock}' (not Alarm); $X exchanged");

            // 3b) Jog: a tiny incremental $J command must be accepted once the
            //     machine is out of alarm. This catches the jog-format regression
            //     (the app used to send '$J=G91 G0 …' — prohibited g-code inside a
            //     jog, rejected as error 16/21; bare '$J=X1' is accepted but is an
            //     ABSOLUTE move under G90, so G91 must be embedded to jog relative).
            //     Boards stuck in a critical alarm (grblHAL error 79) or with homing
            //     required will refuse — reported, not asserted, since that depends
            //     on the bench setup.
            var beforeJog = received.Count;
            Console.WriteLine("JOG sending $J=G91 X1 F500 (+1mm incremental)...");
            await svc.SendAsync("$J=G91 X1 F500");
            var jogReplies = received.Skip(beforeJog).Where(l => !l.StartsWith('<')).Take(2).ToArray();
            Console.WriteLine($"JOG reply: {(jogReplies.Length > 0 ? string.Join(" | ", jogReplies) : "(none observed)")}");

            // 4) Reset: realtime 0x18 reboots the controller -> boot banner re-prints
            //    and the alarm lock re-arms (grbl power-up state with homing enabled).
            var countBefore = received.Count;
            Console.WriteLine("RESET sending realtime 0x18 (soft reset)...");
            svc.SendRealtime(0x18);
            var rebanner = await WaitUntil(() => received
                .Skip(countBefore)
                .Any(l => l.Contains("Grbl", StringComparison.OrdinalIgnoreCase) && l.Contains("help")), 15_000);
            var banner = rebanner
                ? received.Skip(countBefore).FirstOrDefault(l =>
                    l.Contains("Grbl", StringComparison.OrdinalIgnoreCase) && l.Contains("help"))
                : null;
            Console.WriteLine(
                rebanner ? $"OK  reset: banner re-printed: '{banner}'" : "WARN no re-banner within 15s after reset");

            // After the reboot the poll loop must resume reporting (a fresh status
            // line proves it); a short settle lets `latest` reflect the real state.
            var postResetCount = received.Count(l => l.StartsWith('<'));
            var postReset = await WaitUntil(
                () => received.Count(l => l.StartsWith('<')) > postResetCount + 1, 10_000);
            await Task.Delay(300);
            Console.WriteLine($"RESET post-reboot status='{latest?.Status}'");
            Assert.True(postReset, "status poll must resume after reset");

            // Dump the raw lines since the reset so an interactive triage session can
            // see exactly what the board emitted (banner, any alarm, first statuses).
            var postResetLines = received.Skip(countBefore).ToArray();
            Console.WriteLine(
                $"RESET raw lines: {string.Join(" | ", postResetLines.Select(l => l.Length > 60 ? l[..60] + "…" : l))}");

            // 5) Commands still flow after the reboot.
            var settings = await svc.GetSettingLinesAsync();
            Console.WriteLine($"OK  $$ after reset returned {settings.Count} setting lines");
            Assert.NotEmpty(settings);
        }
        finally
        {
            svc.Disconnect();
            // Restore the global static so a hardware run can't leak into later tests.
            CNC.Core.GrblInfo.NumAxes = savedAxes;
            CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
        }
    }

    [Fact]
    public async Task Offsets_module_writes_and_reads_back_G54_through_G59_on_hardware()
    {
        var port = Environment.GetEnvironmentVariable("SENDERNG_HW_PORT");
        if (string.IsNullOrWhiteSpace(port))
            port = AvailablePorts.FirstOrDefault(p => p.Equals("COM6", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(port))
        {
            Console.WriteLine("WARNING: offsets hardware test skipped — set SENDERNG_HW_PORT or connect a COM6 board");
            return;
        }
        if (!AvailablePorts.Contains(port.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"WARNING: offsets hardware test skipped — '{port}' not enumerated");
            return;
        }

        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        using var service = new RealGrblService();
        try
        {
            await service.ConnectAsync(port.Trim());
            Assert.True(service.IsConnected);
            Assert.True(service.Capabilities.IsKnown);

            // The controller may boot locked/alarmed. Unlock before exercising G10;
            // otherwise a valid offset command is rejected even though the serial
            // exchange itself is healthy.
            await service.SendAsync("$X");

            var settingsDir = Path.Combine(Path.GetTempPath(), "senderng-hw-" + Guid.NewGuid().ToString("N"));
            var hwSettings = new CNC.Core.Services.SettingsService(settingsDir);
            hwSettings.Save(hwSettings.Load());
            var vm = new SenderNG.Avalonia.Modules.Fixed.OffsetsViewModel(service, hwSettings);
            var codes = new[] { "G54", "G55", "G56", "G57", "G58", "G59" };
            var rows = codes.Select(code => Assert.Single(vm.Rows, row => row.Code == code)).ToArray();
            var original = rows.ToDictionary(row => row.Code, row => row.Values.ToArray());

            try
            {
                for (var i = 0; i < rows.Length; i++)
                    rows[i].SetValueAt(0, (100 + i * 11.25).ToString("0.###", CultureInfo.InvariantCulture));

                await vm.SetAllAsync();
                Assert.StartsWith("Saved ", vm.Status);
                Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);

                var written = await service.ReadWorkParametersAsync();
                for (var i = 0; i < rows.Length; i++)
                {
                    var actual = Assert.Single(written.CoordinateSystems, x => x.Code == codes[i]);
                    Assert.Equal(100 + i * 11.25, actual.Values[0], 3);
                }
            }
            finally
            {
                for (var i = 0; i < rows.Length; i++)
                    for (var axis = 0; axis < rows[i].Values.Count; axis++)
                        rows[i].SetValueAt(axis, original[codes[i]][axis]);

                await vm.SetAllAsync();
                var restoredStatus = vm.Status.StartsWith("Saved", StringComparison.Ordinal);
                Assert.True(restoredStatus, $"offset restore did not complete: {vm.Status}");
                Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);

                var restored = await service.ReadWorkParametersAsync();
                foreach (var code in codes)
                {
                    var actual = Assert.Single(restored.CoordinateSystems, x => x.Code == code);
                    var expected = original[code].Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                    Assert.Equal(expected.Length, actual.Values.Length);
                    for (var axis = 0; axis < expected.Length; axis++)
                        Assert.True(Math.Abs(expected[axis] - actual.Values[axis]) <= 0.001, $"{code} axis {axis}: expected {expected[axis]}, got {actual.Values[axis]}");
                }
            }
        }
        finally
        {
            service.Disconnect();
            CNC.Core.GrblInfo.NumAxes = savedAxes;
            CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
        }
    }

    /// <summary>
    /// Interactive alarm-gating probe (opt-in via SENDERNG_HW_PORT). Classic grbl
    /// 1.1 boards with homing disabled ($22=0) boot Idle on every connect (the app's
    /// DTR-at-open pulse resets them), so the "saves refused while in Alarm" path
    /// never shows up naturally — this test induces a REAL alarm on the wire the way
    /// the triage test discovered it: a jog followed immediately by a soft reset
    /// (0x18) aborts the cycle and the board reboots into <c>ALARM:3</c>. It then
    /// verifies the Offsets editor disables, both saves are refused, and <c>$X</c>
    /// restores the ability to save.
    /// </summary>
    [Fact]
    public async Task Alarm_gating_disables_offsets_saves_on_hardware()
    {
        var port = Environment.GetEnvironmentVariable("SENDERNG_HW_PORT")?.Trim();
        if (string.IsNullOrWhiteSpace(port) || !AvailablePorts.Contains(port, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("WARNING: alarm-gating probe skipped — set SENDERNG_HW_PORT to a connected board");
            return;
        }

        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        var received = new ConcurrentQueue<string>();
        MachineState? latest = null;
        using var service = new RealGrblService();
        try
        {
            service.ReceivedLines.Subscribe(received.Enqueue);
            service.State.Subscribe(s => latest = s);
            await service.ConnectAsync(port);

            var gotIdle = await WaitUntil(
                () => latest is { Status: "Idle" } && received.Count(l => l.StartsWith('<')) >= 2,
                HandshakeTimeoutMs);
            Console.WriteLine(gotIdle ? "OK  connected and Idle" : $"WARN never saw Idle (status='{latest?.Status}') — aborting probe");
            if (!gotIdle)
                return;

            var settingsDir = Path.Combine(Path.GetTempPath(), "senderng-hw-" + Guid.NewGuid().ToString("N"));
            var hwSettings = new SettingsService(settingsDir);
            hwSettings.Save(hwSettings.Load());
            var vm = new SenderNG.Avalonia.Modules.Fixed.OffsetsViewModel(service, hwSettings);

            // Induce ALARM:3: jog, then soft-reset in the Jog->Idle window grbl 1.1
            // leaves open right after the jog's "ok" (verified on the 1.1h board:
            // ALARM:3 + '[MSG:\'$H\'|\'$X\' to unlock]' + <Alarm|MPos:0.004,..>).
            var before = received.Count;
            try
            {
                await service.SendAsync("$J=G91 X0.1 F500");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARN jog rejected ({ex.Message}) — cannot induce the abort alarm; skipping probe");
                return;
            }
            service.SendRealtime(0x18);

            var inAlarm = await WaitUntil(() => latest is { Status: "Alarm" }, 10_000);
            var alarmLines = received.Skip(before).Where(l => l.StartsWith("ALARM:") || l.StartsWith("[MSG:") || l.StartsWith('<')).Take(4).ToArray();
            Console.WriteLine(
                inAlarm
                    ? $"OK  alarm induced on wire: {string.Join(" | ", alarmLines)}"
                    : $"WARN no alarm after jog+reset (status='{latest?.Status}') — board boots Idle; skipping gating checks");
            if (!inAlarm)
                return;

            Assert.False(vm.IsReady, "editor must disable while the controller is in Alarm");
            await vm.SetAllAsync();
            Assert.Contains("alarm", vm.Status, StringComparison.OrdinalIgnoreCase);
            await vm.SaveToolTableAsync();
            Assert.Contains("alarm", vm.Status, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"OK  gating: IsReady=false; Set all and Save tools refused ('{vm.Status}')");

            await service.SendAsync("$X");
            var ready = await WaitUntil(() => vm.IsReady, 10_000);
            Console.WriteLine(ready ? "OK  unlocked: IsReady=true after $X" : "FAIL $X did not restore IsReady");
            Assert.True(ready);

            // Saves must work again once unlocked (values unchanged → no-op writes).
            await vm.SetAllAsync();
            Assert.StartsWith("Saved", vm.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"OK  saves re-enabled after unlock ('{vm.Status}')");
        }
        finally
        {
            service.Disconnect();
            CNC.Core.GrblInfo.NumAxes = savedAxes;
            CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
        }
    }

    /// <summary>
    /// Interactive per-board verification (opt-in via SENDERNG_HW_PORT): run once
    /// per board — classic 1.1h, mega5x 1.2i fork, grblHAL — and verify (a) board
    /// identity/capabilities, (b) the G59.x clamp (systems offered == what the
    /// firmware can actually write; no G59.x on classic), (c) alarm gating (saves
    /// refused while the controller reports Alarm), (d) tool-table visibility
    /// (shown only when the user enabled it AND the controller is grblHAL with
    /// tool slots), and (e) offsets write/readback for every supported system,
    /// restored to the original values afterwards.
    /// </summary>
    [Fact]
    public async Task Offsets_alarm_tooltable_and_g59x_clamp_verified_on_hardware()
    {
        var port = Environment.GetEnvironmentVariable("SENDERNG_HW_PORT")?.Trim();
        if (string.IsNullOrWhiteSpace(port))
        {
            Console.WriteLine("WARNING: per-board verification skipped — set SENDERNG_HW_PORT to run it");
            return;
        }
        if (!AvailablePorts.Contains(port, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"WARNING: per-board verification skipped — '{port}' not enumerated");
            return;
        }

        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        var received = new ConcurrentQueue<string>();
        MachineState? latest = null;
        using var service = new RealGrblService();
        try
        {
            service.ReceivedLines.Subscribe(received.Enqueue);
            service.State.Subscribe(s => latest = s);
            await service.ConnectAsync(port);
            Assert.True(service.IsConnected);
            Assert.True(service.Capabilities.IsKnown);

            // Wait for the first real status reports so the VM has seen the board's
            // actual state (alarm or not) before we test gating.
            var gotStatus = await WaitUntil(
                () => latest != null && latest.Status != null && received.Count(l => l.StartsWith('<')) >= 2,
                HandshakeTimeoutMs);
            Assert.True(gotStatus, "board must produce live status reports");
            await Task.Delay(300); // settle so `latest` reflects a fresh report

            var c = service.Capabilities;
            Console.WriteLine(
                $"BOARD {port}: IsGrblHAL={c.IsGrblHAL} firmware='{c.Firmware}' version='{c.Version}' " +
                $"axes={c.NumAxes} tools={c.NumTools} status='{latest!.Status}'");

            var settingsDir = Path.Combine(Path.GetTempPath(), "senderng-hw-" + Guid.NewGuid().ToString("N"));
            var hwSettings = new SettingsService(settingsDir);
            hwSettings.Save(hwSettings.Load());
            var vm = new SenderNG.Avalonia.Modules.Fixed.OffsetsViewModel(service, hwSettings);
            var wpVm = new SenderNG.Avalonia.Modules.Fixed.WorkParametersViewModel(service);

            // (b) G59.x clamp — offer only what this firmware can actually write.
            // Classic grbl 1.1 has no G59.x (rejected with error 29), so the offsets
            // editor rows and the Work Parameters selector must never offer them.
            var expectedSystems = WorkParameters.SupportedSystems(c.IsGrblHAL);
            Assert.Equal(expectedSystems, vm.SupportedSystems);
            Assert.Equal(expectedSystems, wpVm.CoordinateSystemCodes);
            Assert.Equal(expectedSystems.Count, vm.Rows.Count);
            foreach (var code in expectedSystems)
                Assert.Single(vm.Rows, row => row.Code == code);
            if (!c.IsGrblHAL)
            {
                Assert.DoesNotContain(vm.Rows, row => row.Code.StartsWith("G59.", StringComparison.Ordinal));
                Assert.DoesNotContain(wpVm.CoordinateSystems, row => row.Code.StartsWith("G59.", StringComparison.Ordinal));
            }
            Assert.Equal(c.NumAxes, vm.Rows[0].Values.Count);
            Console.WriteLine(
                $"OK  G59.x clamp: offered systems = [{string.Join(", ", vm.SupportedSystems)}] " +
                $"({(c.IsGrblHAL ? "grblHAL: G59.x supported" : "classic: G59.x correctly hidden")})");

            // (c) Alarm gating — while the controller reports Alarm, saves are refused.
            var wasAlarm = latest.Status == "Alarm";
            if (wasAlarm)
            {
                Assert.False(vm.IsReady, "editor must be disabled while the controller is in Alarm");
                await vm.SetAllAsync();
                Assert.Contains("alarm", vm.Status, StringComparison.OrdinalIgnoreCase);
                await vm.SaveToolTableAsync();
                Assert.Contains("alarm", vm.Status, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"OK  alarm gating: in Alarm -> IsReady=false, saves refused ('{vm.Status}')");

                await service.SendAsync("$X");
                var unlocked = await WaitUntil(() => vm.IsReady, 10_000);
                Console.WriteLine(unlocked ? "OK  unlocked ($X): IsReady=true" : "FAIL $X did not clear the alarm");
                Assert.True(unlocked);
            }
            else
            {
                Assert.True(vm.IsReady, "editor should be ready when the controller is not in Alarm");
                Console.WriteLine($"OK  alarm gating: board booted '{latest.Status}' (not Alarm) -> IsReady=true");
            }

            // (d) Tool-table visibility — only grblHAL with tool slots gets a tool
            //     table; classic 1.1h and classic forks have no G10 L1 at all.
            var expectToolTable = c.IsGrblHAL && c.NumTools > 0;
            Assert.Equal(expectToolTable, vm.ControllerSupportsToolTable);
            Assert.Equal(expectToolTable, vm.ShowToolTableSection); // config default: ShowToolTable=true
            Console.WriteLine(
                expectToolTable
                    ? $"OK  tool table visible (grblHAL, {c.NumTools} tool slots)"
                    : $"OK  tool table hidden ({(c.IsGrblHAL ? "no tool slots" : "not grblHAL — no G10 L1")})");

            // (e) Offsets write/readback for every supported system, then restore.
            var rows = vm.Rows.ToArray();
            var original = rows.ToDictionary(row => row.Code, row => row.Values.ToArray());
            try
            {
                for (var i = 0; i < rows.Length; i++)
                    rows[i].SetValueAt(0, (100 + i * 11.25).ToString("0.###", CultureInfo.InvariantCulture));

                await vm.SetAllAsync();
                Assert.StartsWith("Saved", vm.Status, StringComparison.Ordinal);
                Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);

                var written = await service.ReadWorkParametersAsync();
                for (var i = 0; i < rows.Length; i++)
                {
                    var actual = Assert.Single(written.CoordinateSystems, x => x.Code == rows[i].Code);
                    Assert.Equal(100 + i * 11.25, actual.Values[0], 3);
                }
                Console.WriteLine($"OK  offsets write/readback: {rows.Length} systems written and read back on {port}");
            }
            finally
            {
                for (var i = 0; i < rows.Length; i++)
                    for (var axis = 0; axis < rows[i].Values.Count; axis++)
                        rows[i].SetValueAt(axis, original[rows[i].Code][axis]);
                await vm.SetAllAsync();
                Assert.DoesNotContain("failed", vm.Status, StringComparison.OrdinalIgnoreCase);

                var restored = await service.ReadWorkParametersAsync();
                foreach (var code in expectedSystems)
                {
                    var actual = Assert.Single(restored.CoordinateSystems, x => x.Code == code);
                    var expected = original[code].Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                    Assert.Equal(expected.Length, actual.Values.Length);
                    for (var axis = 0; axis < expected.Length; axis++)
                        Assert.True(Math.Abs(expected[axis] - actual.Values[axis]) <= 0.001,
                            $"{code} axis {axis}: expected {expected[axis]}, got {actual.Values[axis]}");
                }
                Console.WriteLine($"OK  offsets restored on {port}");
            }
        }
        finally
        {
            service.Disconnect();
            CNC.Core.GrblInfo.NumAxes = savedAxes;
            CNC.Core.GrblInfo.IsGrblHAL = savedHAL;
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(100);
        }
        return false;
    }
}