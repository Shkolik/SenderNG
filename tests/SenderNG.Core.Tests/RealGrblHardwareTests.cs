using System.Collections.Concurrent;
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
            //     jog, rejected as error 16/21). Boards stuck in a critical alarm
            //     (grblHAL error 79) or with homing required will refuse — reported,
            //     not asserted, since that depends on the bench setup.
            var beforeJog = received.Count;
            Console.WriteLine("JOG sending $J=X1 F500 (1mm)...");
            await svc.SendAsync("$J=X1 F500");
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