# AGENT.md — SenderNG

Guidance for AI agents (and human contributors) making changes to SenderNG. This applies to any **non-trivial change**: a bug fix, a behavior or API change, a refactor, or any edit touching multiple files. Trivial edits (typos, comments, one-line fixes) can skip the ceremony but must still follow the repo's conventions.

## Workflow for non-trivial changes

1. **Investigate first.** Read the existing implementation, its callers, and its tests before proposing anything. Follow the project's established conventions (namespaces, xUnit style, nullable reference types enabled).
2. **Explain the approach before editing.** Briefly state what you plan to change and why, then adjust the plan as you learn. Don't write code before you can say what it's for.
3. **Reproduce bugs before fixing them.** Confirm the failure first. Where feasible, reproduce with a failing test; otherwise capture a minimal manual repro and describe it.
4. **Demonstrate the failure with a test.** Write a test that fails against the current code, then make it pass. Tests live in `tests/SenderNG.Core.Tests` (xUnit).
5. **Keep changes minimal.** Fix the problem, not the neighborhood: no unrelated refactoring, no style churn, and no work on deferred modules (Lathe, DragKnife, Converters) unless explicitly asked.
6. **Run the relevant checks.** At minimum:
   - Build: `dotnet build SenderNG.sln`
   - Tests: `dotnet test SenderNG.sln` (or `dotnet test --filter <name>` for a targeted run)
7. **Review the final diff.** Re-read the change (`git diff`) for correctness and unintended edits before finishing.
8. **Commit and merge.** After each successfully finished pass/turn, commit the change on the feature branch and merge it into `main` before ending the turn. From the main worktree, merge the feature-branch tip by explicit commit hash — `@`/`HEAD` resolve to main's own HEAD there and silently no-op. Use conventional commit messages.
9. **Re-verify after merge.** Re-run build and tests at the merged state when the binaries are not locked by a running app; if they are, state that the identical commit already passed in the worktree.

## Project facts

- .NET 10 / Avalonia cross-platform gcode sender; fork of ioSender (BSD 3-Clause). SDK pinned in `global.json`.
- The original ioSender reference checkout is available locally at `c:\Projects\Workshop\ioSender\` (engine in `CNC Core/CNC Core/`, WPF UI in `CNC Controls/CNC Controls/` and `ioSender/ioSender/`). Use it for reference instead of scraping the web; mirror ioSender's semantics where behavior parity matters.
- Three real test boards are available when hardware verification is needed: Mega 2560, RP2040, and Arduino Nano v3. Before running hardware-dependent tests, explicitly prompt the user to connect the specific board required and provide its port when applicable; never assume a board is connected.
- `src/SenderNG.Core` — engine (namespaces `CNC.GCode`, `CNC.Core`, `SenderNG.*`); `src/SenderNG.Avalonia` — UI; `tests/SenderNG.Core.Tests` — xUnit tests.
- Deferred scope (don't implement or extend without being asked): Lathe wizards, DragKnife, Converters.
- The build currently emits warnings (CS8602, AVLN5001, xUnit1031, …); don't add new ones beyond the scope of the change.

## Verification limits

- Hardware-dependent paths (serial, Ethernet/Telnet/WebSocket, machine control) can't be fully exercised by unit tests. State what was and wasn't verified.
- For UI-only changes, say so and describe how you validated the behavior when automated tests don't apply.
