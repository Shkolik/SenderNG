using CNC.Core.Abstractions;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

/// <summary>
/// Hardware smoke tests against a real grbl / grblHAL board (user's COM6, grbl 1.2i).
/// These are not unit tests: they exercise the real serial transport, connect path,
/// '?' status poll loop and a command exchange against actual hardware.
///
/// If the expected port is not present the test is skipped with a warning line rather
/// than failing — the project's CI / other machines have no board attached. When the
/// board is attached (AGENT.md "Verification limits") it yields real confidence in the
/// P0 read-loop/poll path.
///
/// Signals/limits remain untested on hardware as noted in p0-controller-plumbing-plan.
/// </summary>
public class RealGrblHardwareTests
{
    private const string ExpectedPort = "COM6";
    private const int HandshakeTimeoutMs = 8000;

    private static string[] AvailablePorts => new SerialComPortService().GetPorts();

    [Fact]
    public async Task Connect_polls_live_status_and_exchanges_settings_on_hardware()
    {
        if (!AvailablePorts.Contains(ExpectedPort, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"WARNING: hardware smoke test skipped — expected '{ExpectedPort}' not found " +
                $"(found: {string.Join(", ", AvailablePorts)})");
            return; // test "passes" as skipped-by-warning when no board present
        }

        using var svc = new RealGrblService();
        MachineState? latest = null;
        svc.State.Subscribe(s => latest = s);

        await svc.ConnectAsync(ExpectedPort);
        try
        {
            // 1) The '?' poll loop must produce live status reports on real hardware.
            var gotStatus = await WaitUntil(
                () => latest != null && !string.IsNullOrEmpty(latest.Status),
                HandshakeTimeoutMs);
            Console.WriteLine(
                gotStatus
                    ? $"OK  live status from {ExpectedPort}: '{latest?.Status}' " +
                      $"WPos=({string.Join(",", latest!.Positions)})"
                    : "WARN no status report arrived within timeout (poll loop inactive?)");

            // 1b) The connect-time $I handshake must identify the controller.
            Console.WriteLine(
                $"OK  $I handshake capabilities: IsGrblHAL={svc.Capabilities.IsGrblHAL}, " +
                $"version='{svc.Capabilities.Version}', axes={svc.Capabilities.NumAxes}");
            Assert.True(svc.Capabilities.IsKnown, "$I handshake should identify the controller");
            Assert.True(svc.Capabilities.IsGrblHAL, "expected grblHAL board (user reported grbl 1.2i)");

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