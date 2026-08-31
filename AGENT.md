# AGENT.md — SenderNG

Guidance for AI agents (and human contributors) making changes to SenderNG. This applies to any **non-trivial change**: a bug fix, a behavior or API change, a refactor, or any edit touching multiple files. Trivial edits (typos, comments, one-line fixes) can skip the ceremony but must still follow the repo's conventions.

## Workflow for non-trivial changes

1. **Investigate first.** Read the existing implementation, its callers, and its tests before proposing anything. Follow the project's established conventions (namespaces, xUnit style, nullable reference types enabled).
2. **Explain the approach before editing.** Briefly state what you plan to change and why, then adjust the plan as you learn. Don't write code before you can say what it's for.
3. **Reproduce bugs before fixing them.** Confirm the failure first. Where feasible, reproduce with a failing test; otherwise capture a minimal manual repro and describe it.
4. **Demonstrate the failure with a test.** Write a test that fails against the current code, then make it pass. Tests live in `tests/SenderNG.Core.Tests` (xUnit).
5. **Keep changes minimal.** Fix the problem, not the neighborhood: no unrelated refactoring, no style churn, and no work on deferred modules (Lathe, DragKnife, Converters) unless explicitly asked.
6. **Add a copyright header to every new file.** Every new `src` file you add must start with the correct per-file copyright header chosen by the rules in `docs/copyright-headers.md` (mine-only vs. double vs. untouched). Do this at creation time, not later.
7. **Run the relevant checks.** At minimum:
   - Build: `dotnet build SenderNG.sln`
   - Tests: `dotnet test SenderNG.sln` (or `dotnet test --filter <name>` for a targeted run)
8. **Review the final diff.** Re-read the change (`git diff`) for correctness and unintended edits before finishing.
9. **Leave the change uncommitted.** Do not `git commit`, merge, or push on your own. Keep finished work local in the working tree unless the maintainer explicitly asks for a commit/merge (and provide the exact commands they'd use).
10. **State verification honestly.** Re-run build and tests on the current state for any non-trivial change (or when binaries are not locked by a running app). Report exactly what was and wasn't verified; never imply a commit or merge happened when one did not.

## Project facts

- .NET 10 / Avalonia cross-platform gcode sender; fork of ioSender (BSD 3-Clause). SDK pinned in `global.json`.
- The original ioSender reference checkout is available locally at `c:\Projects\Workshop\ioSender\` (engine in `CNC Core/CNC Core/`, WPF UI in `CNC Controls/CNC Controls/` and `ioSender/ioSender/`). Use it for reference instead of scraping the web; mirror ioSender's semantics where behavior parity matters.
- **Hardware testing** — three real test boards are on the bench, nothing wired to them yet (RP2040 grblHAL, Mega 2560 mega5x grbl 1.2i, Arduino Nano classic grbl 1.1). They are safe to exercise (settings/parameters may be changed as needed) and tests restore what they touch. Boards' COM ports are not stable — resolve via `SENDERNG_HW_PORT=<port>` or auto-scan. See `docs/hardware-testing.md` for the board→firmware matrix, port discovery, and how to run the automated + interactive hardware tests. Before running any hardware-dependent test, explicitly prompt the user to connect the specific board and provide its port; never assume a board is connected, and check which board is actually plugged in (the firmware class changes what the test must verify).
- `src/SenderNG.Core` — engine (namespaces `CNC.GCode`, `CNC.Core`, `SenderNG.*`); `src/SenderNG` — UI; `tests/SenderNG.Core.Tests` — xUnit tests.
- **Copyright headers** — every file carries a per-author copyright header (mine-only / double / untouched). The exact forms and the rule for choosing one are in `docs/copyright-headers.md`; follow it for all new files.
- **Avalonia UI gotchas** — master/detail editor panels reuse one set of Controls across items, and recycled Controls fire Change events with stale state during a selection switch; never write from an editor's event handler without guarding by editor type (see `docs/avalonia-gotchas.md`). Read it before touching any editor-driven widget.
- Deferred scope (don't implement or extend without being asked): Lathe wizards, DragKnife, Converters.
- The build currently emits a small set of warnings (mostly xUnit1031, a few CS86xx nullability odds and ends); don't add new ones beyond the scope of the change.

## Verification limits

- Hardware-dependent paths (serial, Ethernet/Telnet/WebSocket, machine control) can't be fully exercised by unit tests. State what was and wasn't verified.
- For UI-only changes, say so and describe how you validated the behavior when automated tests don't apply.
