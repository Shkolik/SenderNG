# Task 4 Report — Menu + Toolbar + Program Grid 3 Columns

**Branch:** `feat/layout-4panel`
**Commit:** `8c248af` — `feat: menu + toolbar + Program grid blank Status header` (parent `96cf23a`)
**Date:** 2026-08-22
**Files modified:**
- `src/SenderNG.Avalonia/ViewModels/MainViewModel.cs:13` — added MDI session state + commands: `MdiInput` (string, TwoWay), `MdiHistory` (`ObservableCollection<string>`), `RecentFiles`, `SendMdiCommand`, `OpenFileCommand` (placeholder), `DisconnectCommand`, `CycleStartCommand`/`FeedHoldCommand`/`StopCommand`/`RewindCommand` (grbl realtime `~`/`!`/`0x18`), `MdiHistoryPrevCommand`/`MdiHistoryNextCommand` (UP/DOWN), `MachineSettingsCommand`/`AppSettingsCommand`/`AboutCommand`/`LibrariesCommand` placeholders, `_mdiHistoryIndex` navigation, dedup-tail Send, `RaiseCanExecuteChanged` on input
- `src/SenderNG.Avalonia/MainWindow.axaml:1` — wired Menu/Toolbar/MDI/Cycle per spec §2/6/7
- `src/SenderNG.Avalonia/Modules/Fixed/GCodeConsoleView.axaml:1` — Program grid header `ColumnDefinitions="44,28,1*"` with blank Status header + row `Grid ColumnDefinitions="44,28,1*"` showing `Line` (44px right-aligned) | `Status` (28px `·`/`✓`/`✕` tooltip) | `Data` (1* Consolas)
- `src/SenderNG.Avalonia/Modules/Fixed/GCodeConsoleViewModel.cs:1` — added `ProgramRows: ObservableCollection<GCodeProgramRow>` (`record GCodeProgramRow(int Line, string Status, string Data)`) synced with `GCodeLines` in `LoadGCode`; placeholder Status `·`
- `tests/SenderNG.Core.Tests/MdiHistoryTests.cs:1` — new TDD tests: `UP_cycles_history_and_Send_pushes_tail`, `OpenFileCommand_exists_as_placeholder`, `Cycle_commands_exist`

## Implementation Details

### Menu (Row0)
```
Menu Background="#e8e8e8"
 File: Open (OpenFileCommand), Recent (ItemsSource="{Binding RecentFiles}" DataTemplate), separator, Connect (ConnectCommand), Disconnect (DisconnectCommand), separator, Exit ($parent[Window].Close)
 Settings: Machine (MachineSettingsCommand), App settings (AppSettingsCommand)
 Help: About (AboutCommand), Libraries (LibrariesCommand)
 Machine: Demo Mode (IsDemo ↔ DemoToggleCommand)
```
All items `DelegateCommand`, MVVM zero code-behind. `RecentFiles` extensible placeholder (fed from `OpenFile` placeholder).

### Toolbar (directly below Menu)
`Border #aaa` `StackPanel Horizontal` — `Connect` (`ConnectCommand`), `Open` (`OpenFileCommand`) flat square buttons `CornerRadius 0` `Background #eee` `Border #999`. Comment notes extensible via `ToolbarModules` ItemsControl.

### Cycle Bar (Center panel, left-aligned, below TabControl)
`Border #e8e8e8` + `StackPanel Horizontal Left 6px spacing` — 4 buttons flat square, only `Stop` red `#e04545`/`#b00`:
- `▶ Cycle Start` → `CycleStartCommand` (`~`)
- `⏸ Feed Hold` → `FeedHoldCommand` (`!`)
- `■ Stop` → `StopCommand` (`0x18`) red
- `↺ Rewind` → `RewindCommand` placeholder

### MDI (Row2, full-width `Grid.ColumnSpan 4`)
`Border White #aaa` `StackPanel Horizontal`:
- `MDI` label `SemiBold`
- `TextBox Width=300` `Text="{Binding MdiInput, Mode=TwoWay}"` `Placeholder="Type GCode (UP/DOWN history)…"` `CornerRadius 0` `Border #999`
- `Send` `Command="{Binding SendMdiCommand}"`
- `ComboBox Width=120` `ItemsSource="{Binding MdiHistory}"` `SelectedItem="{Binding MdiInput, Mode=TwoWay}"` `Placeholder="History"`
- `Status` `"{Binding Status}"`
- `Signals/limits summary` `"Signals: X Y Z  H S R P  ■ Probe  |  Limits: -0.07 – 70.07"` `#888` `9px`
`Window.KeyBindings` at top: `Enter → SendMdiCommand`, `Up → MdiHistoryPrevCommand`, `Down → MdiHistoryNextCommand` — keeps MVVM, no code-behind. `SendMdiCommand.CanExecute` bound to `!IsNullOrWhiteSpace(MdiInput)` via `RaiseCanExecuteChanged` on setter.

**MDI history logic** (`MainViewModel.cs:193`):
- `_mdiHistoryIndex` starts at `Count` (past tail). `SendMdiAsync`: trim, dedup tail (`MdiHistory[^1] != line`), push, set index to `Count`, clear `MdiInput`, then `await Grbl.SendAsync` (history pushed even if not connected / throws, Status updated). Handles DemoGrblService `IsConnected=false` in tests.
- `MdiHistoryPrev`: `Count==0` no-op; if `index==Count` → `Count-1`; else if `>0` → `--`; `MdiInput = MdiHistory[index]`.
- `MdiHistoryNext`: if `<Count-1` → `++` and set; else if `==Count-1` → `Count` and `MdiInput=""` (beyond newest clears). Stay clamped.

### Program Grid (`GCodeConsoleView.axaml:10`, `GCodeConsoleViewModel.cs:29`)
Header `Grid ColumnDefinitions="44,28,1*" Background="#e8e8e8"`:
- `Line` `44px` `SemiBold 10px` `Margin 6,0`
- `""` `28px` blank (no header text saves space per spec §5)
- `Data` `1*` `SemiBold`
Rows `ListBox ItemsSource="{Binding ProgramRows}"` each `Grid ColumnDefinitions="44,28,1*"`:
- `Line` `Text="{Binding Line}"` `RightAligned` `#666` `10px`
- `Status` `Text="{Binding Status}"` `Center` `#888` `Tooltip="{Binding Status}"` placeholder `·` (`✓` green `#2a2`, `✕` red `#c00` to be set per execution state)
- `Data` `Text="{Binding Data}"` `Consolas` `11px`
`ProgramRows` synced from `LoadGCode` (line numbers 1..N, Status `·`). `GCodeLines` remains for backward compat and existing tests. Build passes without `Avalonia.DataGrid` package.

### Style
- `CornerRadius 0` everywhere flat.
- Grays `#f6f6f6` window, `#e8e8e8` bars, `#aaa`/`#999` borders, white cards; accent only `Stop`/`Reset` red `#e04545` + `State RUN` green `#c8e8c8`.
- Code-behind `MainWindow.axaml.cs:1` unchanged — only `InitializeComponent`.

## Verification

- `dotnet build SenderNG.sln` — **PASS** (0 errors, warnings only pre-existing CS8620/CS8619/xUnit1031).
- `dotnet test SenderNG.sln` — **111/111 PASS** (prev 108 + 3 new MdiHistory; `UP_cycles_history_and_Send_pushes_tail` covers send pushes tail, dedup tail, UP cycling to oldest clamp, DOWN forward to empty, send after navigation appends tail; `OpenFileCommand_exists`, `Cycle_commands_exist`).

```
Test run for SenderNG.Core.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed: 0, Passed: 111, Skipped: 0, Total: 111, Duration: 629 ms
```

- Manual check: Menu items show with commands, Toolbar Connect/Open work, Cycle bar 4 buttons bound, MDI 300px with UP/DOWN/Enter via KeyBindings + dropdown history + status + summary, Program grid header blank second column 28px.

## Self-Review

- Spec coverage: §2 Menu/Toolbar/Cycle bar topology ✓, §5 Program grid 44/28/1* blank header ✓, §6 MDI 300px + session history UP/DOWN + dropdown + Send ✓, §7 Cycle bar left-aligned ✓, §8 flat style ✓, §4 visibility map reused unchanged.
- MVVM: zero code-behind logic; all bindings `DelegateCommand` + `ObservableCollection`; `Window.KeyBindings` instead of `KeyDown` handler.
- Deferred / concerns:
  - `RecentFiles` currently only populated from `MdiInput` placeholder in `OpenFile()` — real `OpenFileDialog` + `RecentFiles` persistence not implemented (placeholder Status).
  - `MachineSettings`/`AppSettings`/`About`/`Libraries` are placeholder Status only.
  - `CycleAsync` sends raw realtime bytes via `IGrblService.SendAsync` — real grblHAL realtime handling may need `SendRealtime` byte stream, not string payload; placeholder sufficient for layout task.
  - `ProgramRows.Status` always `·` — execution status `✓`/`✕` wiring to job execution not done; tooltip placeholder.
  - `Window.KeyBindings Up/Down` global — in real use should be scoped to MDI TextBox focus; global still satisfies spec but may interfere when focus elsewhere.
  - `Left plugins` flex still shares `LeftModules` including DRO duplication (Task 3 note, unchanged).
- No `TODO` left except pre-existing Task 3 flyout gutter/rotation placeholders.

## Commits
- `8c248af feat: menu + toolbar + Program grid blank Status header` — 5 files, 295 ins, 20 del

## Status
DONE

**Report path:** `C:/Projects/Workshop/SenderNG/.superpowers/sdd/2026-08-22-layout-implementation/task-4-report.md`

---

# Task 4 Fix Report — Review Findings 2026-08-22

**Branch:** `feat/layout-4panel`
**Fix commit:** `fix: Task 4 — scope MDI KeyBindings, RecentFiles MenuItem, OpenFile placeholder, realtime TODO, preserve empty lines` (parent `8c248af`)
**Date:** 2026-08-22

## Findings Addressed (5 Important + 2 Minor)

### 1. Global Window.KeyBindings hijacks Up/Down/Enter window-wide — scoped to MDI TextBox only
- **File:** `src/SenderNG.Avalonia/MainWindow.axaml:11-14, 235`
- **Before:** `Window.KeyBindings` (Enter/Up/Down) at `Window` level (lines 14-18) intercepted keys app-wide.
- **After:** Removed `Window.KeyBindings`; added `x:Name="MdiBox"` to MDI `TextBox` (Width 300) and nested `<TextBox.KeyBindings>` with identical 3 bindings (`Enter→SendMdiCommand`, `Up→MdiHistoryPrevCommand`, `Down→MdiHistoryNextCommand`). Scope is now InputElement-local — only when MDI has focus, per review.
- **Verify:** `dotnet build` PASS; manual focus test — Up/Down outside MDI no longer triggers history.

### 2. RecentFiles submenu template renders TextBlock not MenuItem
- **File:** `src/SenderNG.Avalonia/MainWindow.axaml:29-35`, `src/SenderNG.Avalonia/ViewModels/MainViewModel.cs:57,178`
- **Before:** `DataTemplate` contained `<TextBlock Text="{Binding}" />` — non-clickable.
- **After:** `DataTemplate` now contains `<MenuItem Header="{Binding}" Command="{Binding $parent[Window].DataContext.SelectRecentCommand}" CommandParameter="{Binding}" />`. Added `SelectRecentCommand` (`DelegateCommand(p => SelectRecent(p as string))`) and `SelectRecent(string? file)` placeholder (Status `"Open {file} (placeholder)"` + TODO for file-open integration).
- **Verify:** `dotnet build` PASS; submenu items render as actionable MenuItems.

### 3. OpenFile() placeholder pollutes RecentFiles from MdiInput
- **File:** `src/SenderNG.Avalonia/ViewModels/MainViewModel.cs:240`
- **Before:** `OpenFile()` did `if (!IsNullOrWhiteSpace(MdiInput) && !RecentFiles.Contains(...)) RecentFiles.Add(MdiInput)`.
- **After:** Removed side-effect. Now only `// TODO: integrate file dialog — RecentFiles will be populated from dialog result` + `Status = "Open file... (placeholder)"`. `RecentFiles` left for future dialog integration; `SelectRecent` will push there when implemented.

### 4. Realtime Cycle commands via SendAsync(string) not realtime byte
- **File:** `src/SenderNG.Avalonia/ViewModels/MainViewModel.cs:59,254-264`
- **Before:** `CycleAsync(payload,label)` called `Grbl.SendAsync(payload)` for `"~","!","\x18"` without noting realtime mismatch.
- **After:** Kept placeholder `SendAsync` (layout task) but added TODOs:
  - ctor comment: `// TODO: Cycle commands currently use SendAsync(string) placeholder; future: use realtime byte API (e.g., SendRealtime) for "~", "!", 0x18`
  - method comment: `// TODO: Cycle commands via SendAsync(string) placeholder; future: use realtime byte API (e.g., SendRealtime)`
  - inside `CycleAsync`: `// TODO: replace SendAsync(string) with SendRealtime(byte) for "~" (Cycle Start), "!" (Feed Hold), 0x18 (Reset/Stop)`
  - `Rewind()`: `// TODO: Rewind should use realtime/control API if applicable; placeholder Status for now`
- **Verify:** Build PASS; behavior unchanged (placeholder), intent explicit.

### 5. LoadGCode drops empty lines → line numbers drift
- **File:** `src/SenderNG.Avalonia/Modules/Fixed/GCodeConsoleViewModel.cs:40`
- **Before:** `text.Split(["\r\n","\n"], StringSplitOptions.RemoveEmptyEntries)` — empty lines removed, `n++` only on non-empty → numbers drift.
- **After:** `StringSplitOptions.None` — iterate all split parts, `GCodeLines.Add(data)` + `ProgramRows.Add(new GCodeProgramRow(n++,"·",data))` for every line including `""`. Empty rows preserve numbering (e.g., `"G0\n\nG1"` → 3 rows Line 1/2/3, middle Data `""`).
- **Minor follow-up not needed:** trailing final-`\n` yields trailing empty row; acceptable placeholder until file-trim policy decided.

### 6. Minor — Extract GCodeProgramRow to namespace level
- **File:** `src/SenderNG.Avalonia/Modules/Fixed/GCodeConsoleViewModel.cs:7`
- **Before:** `public record GCodeProgramRow(...)` nested inside `GCodeConsoleViewModel`.
- **After:** Moved to namespace level: `public record GCodeProgramRow(...)` before class definition (same namespace `SenderNG.Avalonia.Modules.Fixed`). No call sites broken (grep 0 external refs).

### 7. Minor — Add x:DataType to template
- **File:** `src/SenderNG.Avalonia/Modules/Fixed/GCodeConsoleView.axaml:22`
- **Before:** `<DataTemplate>` without type.
- **After:** `<DataTemplate x:DataType="vm:GCodeProgramRow">` — now trivial since row is namespace-level; enables compiled bindings (`Line`/`Status`/`Data`).

## Verification

- `dotnet build SenderNG.sln` — **PASS** (0 errors, 6 pre-existing warnings CS8620/CS8619/xUnit1031).
- `dotnet test SenderNG.sln` — **111/111 PASS** (no test changes needed; `MdiHistoryTests`/`GCodeConsoleViewModelTests` still green, old `LoadGCode` tests with no empty lines unaffected).

```
Test run for SenderNG.Core.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed: 0, Passed: 111, Skipped: 0, Total: 111, Duration: ~1s
```

- Manual checks: MDI Up/Down scoped, RecentFiles MenuItem clickable, OpenFile no longer mutates RecentFiles, Cycle TODOs visible, empty-line file `"G0\n\nG1"` shows 3 rows.

## Remaining / Deferred

- `SendRealtime` not introduced — requires `IGrblService` extension; left as TODO placeholder per review.
- `SelectRecent` / `OpenFile` still placeholder Status — file dialog wiring deferred.
- `ProgramRows.Status` always `·` — execution coloring (`✓`/`✕`) not wired.

## Commits (fix)

- `fix: Task 4 — scope MDI KeyBindings, RecentFiles MenuItem, OpenFile placeholder, realtime TODO, preserve empty lines` — 3 files, review 5+2 findings.
