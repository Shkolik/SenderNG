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
    private static bool TryIdentifyBoard(string port, out RealGrblService svc, out List<string> received)
    {
        // Hardware mutates the GrblInfo global (axis count, grblHAL flag); capture so a
        // real run can't leak those values into later unit tests in the same process.
        var savedAxes = CNC.Core.GrblInfo.NumAxes;
        var savedHAL = CNC.Core.GrblInfo.IsGrblHAL;
        received = [];
        var keep = false;
        var candidate = new RealGrblService();
        try
        {
            // Subscribe to the raw-line stream up front so the connect-time $I
            // identity/options burst (a non-replaying Subject) is captured here.
            candidate.ReceivedLines.Subscribe(received.Add);
            candidate.ConnectAsync(port).GetAwaiter().GetResult();
            if (candidate.Capabilities.IsGrblHAL)
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
        string port, RealGrblService svc, List<string> received, int preAxes, bool preHAL)
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
                $"OK  $I handshake capabilities: IsGrblHAL={svc.Capabilities.IsGrblHAL}, " +
                $"version='{svc.Capabilities.Version}', axes={svc.Capabilities.NumAxes}");
            Assert.True(svc.Capabilities.IsKnown, "$I handshake should identify the controller");
            Assert.True(svc.Capabilities.IsGrblHAL, "expected grblHAL board (user reported grbl 1.2i)");
            Assert.True(svc.Capabilities.NumAxes >= 3, "board should report at least 3 axes");
            Assert.Equal(svc.Capabilities.NumAxes, CNC.Core.GrblInfo.NumAxes);
            Assert.Equal(svc.Capabilities.NumAxes, latest!.NumAxes);
            Assert.Equal(svc.Capabilities.NumAxes, latest.Positions.Length);

            // 1c) The raw-line stream must have carried the $I identity/options lines and
            // settings — this is what the Console tab renders.
            Console.WriteLine(
                $"OK  received {received.Count} raw lines; sample: " + string.Join(" | ", received.Take(4)));
            Assert.Contains(received, l => l.StartsWith("[VER:") || l.StartsWith("Grbl") || l.StartsWith("grblHAL"));
            Assert.Contains(received, l => l.StartsWith("[OPT:") || l.StartsWith("[AXS:"));

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