# Hardware testing

SenderNG is verified against three physical grbl controllers on the bench. This document
describes the boards, how the test harness finds their serial ports, and how to run both the
automated and the interactive (human-in-the-loop) hardware tests.

## The three test boards

All three boards are on the bench with **nothing wired to them yet** (no steppers, no endstops,
no probe, no spindle). That means **they are safe to drive with real motion commands only in a
controlled way**, and it is fine — expected, even — to **change settings, coordinate systems,
tool-table entries, or any parameters** as part of a test, because there is no tooling connected
to damage. Every hardware test restores the values it changes (offsets, tools) and the settings
it touches before finishing.

| Board | Firmware | How it identifies | Behaviors it exercises | Notes |
|-------|----------|-------------------|------------------------|-------|
| **RP2040** | **grblHAL** (grbl 1.1 HAL) | `$I` → `IsGrblHAL=true`, banner `Grbl 1.1h`/`grblHAL` | full tool table (`G10 L1` **works**, shows tools), `G54..G59.3` (`G59.x` supported), E-step limits, reference TLO (`$TLR`) | The only board where `G10 L1`/`G43 Hn` round-trips are real. 4 axes, 32 tool slots. |
| **Mega 2560 (mega5x fork)** | **grbl 1.2i** | `$I` → `IsGrblHAL=false`, banner `Grbl 1.2i`, `[AXS:5:XYZAB]` | **5-axes** (XYZAB), reports tool slots in `[OPT:]` but **has NO working `G10 L1`** (`error:20`) — tool table stays hidden | Classic fork that reports `tools=` yet is **not** grblHAL; must be gated as non-HAL (no tool table, no G59.x). |
| **Arduino Nano / legacy classic board** | **classic grbl 1.1** (1.1h) | `$I` → `IsGrblHAL=false`, banner `Grbl 1.1h` | classic behavior: **no G10 L1** (`error:20`), **no G59.x** (`error:29`), boot-to-Alarm with homing, DTR-at-open reset | The "legacy 1.1" board. Most limited; verifies the guards that hide features classic grbl can't do. |

> The megama/legacy/RP2040 split maps to real firmware differences, not just labels. The two
> non-HAL boards are the critical regression targets for **over-offering** features (G59.x rows,
> tool table, jog format) that they would reject with `error:29` / `error:20` / `error:16` / `error:21`.

### What each firmware class must and must not offer

- **grblHAL (RP2040):** G59.x writable, tool table visible & saveable, `G43 Hn` + `G10 L1` for
  the probing tool-length path.
- **classic grbl 1.1 / 1.2i forks:** **no** G59.x, **no** `G10 L1` tool table (hidden), probing
  falls back to `G43.1`. Over-offering G59.x produces `error:29`; offering a tool table produces
  `error:20`.

## Port discovery

A controller's **COM number is not stable** across boots, firmware flashes, or USB re-enumeration
(an unplug/replug can reassign the port). The harness therefore never hard-codes a port, and the
gate never scans ports to choose one: `SENDERNG_HW_PORT` names the single port a test starts
against, with no fallback if it is absent or unusable. The one exception is the bounded reconnect
re-discovery in `Detects_unplug_and_recovers_when_replugged_on_hardware`:
USB re-enumeration can bring the board back on a different port, so after the re-plug that test
re-finds it across physical ports until one answers `$I`.

Every hardware test has three states and no fourth:

- **Not requested** — `SENDERNG_HW_PORT` is unset or whitespace. `[HardwareFact]` skips the test at
  discovery time, so it reports `Skipped` and never opens a port.
- **Requested, unavailable** — the variable is set, but the port is not enumerated, is not a
  physical device (ghost / Bluetooth SPP), never answers the `$I` handshake, or a premise the test
  itself asserts fails. The test reports `Failed` with that specific reason: asking for hardware and
  not getting it is red, not green.
- **Requested, available** — the assertions run against exactly that port.

Selecting a port never involves scanning and has no fallback; the unplug test's bounded reconnect
re-discovery, described above, is the one exception. A test that nobody asked for is `Skipped`;
a test that was asked for and cannot open the port it was given, or that fails a premise it
asserts, is `Failed`. That is why the suite stays green on machines (CI) with no hardware — those
tests are skipped, not passed.

To select only the hardware tests, filter on their category:

```bash
dotnet test tests/SenderNG.Core.Tests --filter "Category=Hardware"   # Skipped unless the env var is set
dotnet test tests/SenderNG.Core.Tests --filter "Category!=Hardware"  # everything except them
```

> Historic COM references in earlier plans (e.g. COM6/COM7) are stale snapshots from old sessions —
> the `COM6` fallback that used to exist in the offsets test is gone.

## The harness

All hardware tests live in `tests/SenderNG.Core.Tests/RealGrblHardwareTests.cs`. They are **not
unit tests**: they open the real serial transport, run the `?` status poll, exchange `$$`/`$I`,
and drive actual machine-control commands against the board. Each test constructs its own
`GrblInfoState` and hands it to `RealGrblService`, so a connect's capability writes land on the
isolated instance and never touch process-global state (2026-09-19: the old save-in-setup /
restore-in-`finally` of the `CNC.Core.GrblInfo` static was removed — the instance replaced it).

They are opt-in via the `SENDERNG_HW_PORT` gate:

```bash
# run all hardware tests against a specific board
SENDERNG_HW_PORT=COM9 dotnet test tests/SenderNG.Core.Tests \
  --filter "Category=Hardware"
```

## Automated (no human) hardware tests

These run unattended against the board named by `SENDERNG_HW_PORT`:

- **`Connect_polls_live_status_and_exchanges_settings_on_hardware`** — `$I` handshake on the
  pinned port, live `?` reports, raw console lines, `$$` round-trip.
- **`Offsets_module_writes_and_reads_back_G54_through_G59_on_hardware`** — cleans up after itself;
  pinned port only. Bet a board is already wired to nothing: offsets are safe to write + restore.
- **`Toggle_dtr_reset_on_connect_is_non_destructive_on_hardware`** — verifies the DTR-pulse
  connect path doesn't brick the connection (targets 8-bit Arduino bootloaders).
- **`Streams_modal_program_with_ok_replies_on_hardware`** — streams a tiny no-motion modal program
  (`G90\nG21`) through `GCodeProgramStreamer`, expecting per-line ok replies while the `?` poll
  continues during the run.

## Interactive (human-in-the-loop) hardware tests

These pause and print prompts (`>>>`) telling you what to do on the bench — plug/unplug cable,
confirm alarm states, and swap boards between runs. Run one board at a time, **always with
`SENDERNG_HW_PORT` set**, then swap.

- **`Detects_unplug_and_recovers_when_replugged_on_hardware`** — you unplug the USB cable, verify
  the app flips to NOT CONNECTED, then re-plug (possibly on a *different* COM port) and verify a
  clean reconnect.
- **`Triage_board_identity_unlock_reset_on_hardware`** — identify the board, verify live status +
  console burst, exercise **Unlock (`$X`)** and **Reset (realtime 0x18)**, then jog. Run once per
  board.
- **`Alarm_gating_disables_offsets_saves_on_hardware`** — induces a real `ALARM:3` on the wire
  (jog then soft-reset), verifies the Offsets editor disables and both saves are refused, then
  `$X` restores saving.
- **`Offsets_alarm_tooltable_and_g59x_clamp_verified_on_hardware`** — the per-board verification
  battery: identity + capabilities, the **G59.x clamp** (only what the firmware can write), **alarm
  gating**, **tool-table visibility** (grblHAL only), and **offsets write/readback + restore**.
  This is the one to run on **each** of the three boards to confirm the board matrix behaves as the
  table above says.

## Interactive session flow (how we actually run the 3-board sweep)

1. Prompt the user which board to connect and wait — never assume a board is already connected.
2. User plugs the board and tells you its COM port.
3. Run the per-board verification with `SENDERNG_HW_PORT=<port>`.
4. Report results, then prompt to swap to the next board and repeat.

One real-world caveat encoded in the tests: **grblHAL native-USB boards (RP2040)** in a critical
alarm (hard/soft limit, e-stop, motor fault) can refuse `$X` with **error:79** — a physical reset
after clearing the cause is required to unlock them. With no limit switches wired, floating inputs
can read as tripped; clearing that float (e.g. `$5`/`$14` inversion to the normally-open state) is
what unlocks the board again. See `docs/p0-controller-plumbing-plan.md` for the triage history.