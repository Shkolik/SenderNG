# Task 3 Report — MainWindow 4-Panel Dock + Collapsible + Cycle Bar

**Branch:** `feat/layout-4panel`
**Commit:** `3d948f2` — `feat: 4-panel MainWindow dock (Left/Center/Right/Flyout + Cycle bar)` (parent `a809379`)
**Date:** 2026-08-22
**Files modified:**
- `src/SenderNG.Avalonia/MainWindow.axaml:1` — replaced `DockPanel`+`Grid ColumnDefinitions="220,*"` sidebar with 4-panel `Grid ColumnDefinitions="220,*,260,32" RowDefinitions="Auto,*,Auto,Auto"` per spec §2 / brief Step 3
- `src/SenderNG.Avalonia/Converters/Converters.cs:1` — new `HasElementsConverter` (`IValueConverter`) handling `int`/`ICollection`/`IEnumerable`; static `Converters.HasElements` for flyout visibility (spec required `FlyoutModules.Count` converter but `IEnumerable` has no `Count` property — adapted to `FlyoutModules` + `HasElements`)
- `src/SenderNG.Avalonia/MainWindow.axaml.cs:1` — unchanged (only `InitializeComponent`), verified MVVM zero code-behind

## Implementation Details

### Grid Topology (exact per brief)
```
Grid ColumnDefinitions="220,*,260,32" RowDefinitions="Auto,*,Auto,Auto"
 Row0 Col0/4 : Menu + Toolbar (File: Open/Recent/Connect/Disconnect/Exit, Settings: Machine/App, Help: About/Libraries + Machine/Demo Mode preserved) + toolbar Border with Connect/Open flat buttons below menu — Background #e8e8e8, Border #aaa, CornerRadius 0
 Row1 Col0   : Left Border (Background White, Border #aaa) with inner Grid RowDefinitions="Auto,Auto,*,Auto,Auto" — 5 Expanders:
               0:DRO, 1:Program limits, 2:Left plugins (flex:1, VerticalAlignment Stretch, ItemsControl ItemsSource="{Binding LeftModules}" filtered getter), 3:Signals, 4:State/Controls (State RUN #c8e8c8, Check, Home/Unlock/Reset red #e04545 only)
 Row1 Col1   : Center Border with Grid RowDefinitions="*,Auto" — TabControl ItemsSource="{Binding CenterModules}" (ItemTemplate Name, ContentTemplate "... view placeholder") + pinned Cycle bar Border below TabControl (left-aligned StackPanel: Cycle Start, Feed Hold, Stop red #e04545/#b00, Rewind, all CornerRadius 0, Background #eee / #e0e0e0 bars)
 Row1 Col2   : Right Border ScrollViewer + ItemsControl ItemsSource="{Binding RightModules}" each as collapsible Expander Header="{Binding Name}" IsExpanded True CornerRadius 0
 Row1 Col3   : Flyout Border Vertical Strip Background #e0e0e0 Width 32 via ColumnDefinition, IsVisible="{Binding FlyoutModules, Converter={x:Static conv:Converters.HasElements}}" (only if >=1 claimant), ItemsControl ItemsSource="{Binding FlyoutModules}" vertical StackPanel
 Row2 Col0/4 : MDI Border full-width: TextBox Width=300 PlaceholderText="Type GCode (UP/DOWN history)…" CornerRadius 0 + Send flat Button + ComboBox History dropdown + Status TextBlock {Binding Status}
 Row3 Col0/4 : StatusBar Border Background #e8e8e8 Border #aaa : Block --/-- | Ready | Jog step 0.05
```
Left plugins reuses `LeftModules` filtered getter (DRO 10 + Offsets 30 will appear there; duplication with fixed DRO placeholder is intentional per brief "or reuse LeftModules" — acceptable for v0).

### Style
- `Background="#f6f6f6"` window canvas, bars `#e8e8e8`, borders `#aaa`/`#999`, white cards `#fafafa`, accent ONLY on `Stop`/`Reset` `Background="#e04545"` `BorderBrush="#b00"` `Foreground="White"` — no other accent.
- Every `Button`, `Border`, `Expander`, `TextBox`, `ComboBox` has `CornerRadius="0"` (square flat per spec §8).
- `Expander.IsExpanded="True"` default, collapsible header click.

### Converters
- `src/SenderNG.Avalonia/Converters/Converters.cs:6` static `Converters.HasElements` wraps `HasElementsConverter: IValueConverter` that returns `i>0` for `int`, `Count>0` for `ICollection`, `MoveNext()` for `IEnumerable`. Used for flyout. Chose binding to `FlyoutModules` (not `.Count`) because `IEnumerable<IModule>` has no `Count` property — AVLN2000 error fixed. Brief's `FlyoutModules.Count` would require `ObservableCollection` or `IReadOnlyList`; current `MainViewModel.FlyoutModules: IEnumerable` → converter on collection itself is correct and preserves spec intent (show only if ≥1 Flyout claimant).

### Code-behind
- `MainWindow.axaml.cs:1` unchanged — only `InitializeComponent()`. No logic added.

## Verification

- `dotnet build SenderNG.sln` — **PASS** (0 errors, 6 warnings only pre-existing: Watermark obsolete removed, CS8620/CS8619 in Jog/Probing VMs, xUnit1031 in Grbl tests). Previously 1 error AVLN2000 on `Count` resolved.
- `dotnet test` — **108/108 PASS** (same as tasks 1-2, duration 829ms). No regressions.

```
Test run for SenderNG.Core.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed: 0, Passed: 108, Skipped: 0, Total: 108
```

- Manual preview: layout topology matches `.superpowers/brainstorm/715-1787416207/content/layout-v7.html` — Left plugins `flex:1` pushes Signals/State to bottom, Center TabControl + left-aligned Cycle bar, Right plugins collapsible, Flyout 32px hidden unless Macro present, MDI 300px, StatusBar full-width.

## Self-Review

- Spec coverage: §2 topology fully mapped; §3 PanelHint consumed via Left/Center/Right/Flyout getters; §4 visibility via IsModuleVisible filtered collections; §6 MDI 300px; §7 Cycle bar left-aligned 4 buttons; §8 flat style square corners + muted grays + red only Stop/Reset.
- Deferred / concerns:
  - Left plugins shows all `LeftModules` including DRO text duplication vs fixed DRO Expander — brief allows reuse; proper fix would be separate `LeftPluginModules` filtered to exclude fixed DRO or show only Offsets etc. Minor cosmetic, non-blocking.
  - Center TabControl currently shows placeholder `TextBlock "{Name} — view placeholder"` instead of real module view (`module.CreateView`) — requires `Func<IModule,object>` view factory converter or `CenterModuleViews` property. Acceptable for layout topology task; view hosting to be wired in Task 4 polish.
  - Flyout vertical text uses `RotateTransform 90°` on Button — Avalonia vertical `writing-mode` not available; rotation achieves similar effect but layout not pixel-perfect vertical-rl.
  - Right panel fixed headers (Work Parameters, Coolant, Spindle, Feed, Rapids) not yet separate from plugin Expanders — currently all RightModules (Jog 15, Cutting 25, Coolant 35, GrblSettings 40, Probing 50) appear as generic Expanders; spec's named sections are placeholders to be replaced by real module views.
- No placeholder `TODO`/`NotImplemented` left in modified files.

## Commits
- `3d948f2 feat: 4-panel MainWindow dock (Left/Center/Right/Flyout + Cycle bar)` — 2 files, 253 ins(+), 36 del(-)

## Review Fix (2026-08-22) — Converter Dispose + Singleton Dedup

**Commit:** (pending) `fix: Task 3 — converter enumerator dispose and fallback, deduplicate singletons`
**Findings addressed:**
1. `Converters.HasElementsConverter` enumerator leak (`src/SenderNG.Avalonia/Converters/Converters.cs:22`) — `e.GetEnumerator().MoveNext()` created without dispose. Fixed via `HasAny(IEnumerable e)` helper with `try/finally (en as IDisposable)?.Dispose()` (pattern per review: `using var en = e.GetEnumerator(); return en.MoveNext();`). Also changed fallback `_ => true` → `_ => false` (`Converters.cs:24`) so unknown types do not falsely show Flyout.
2. Duplicate static instance (`Converters.cs:7,13`) — `Converters.HasElements = new HasElementsConverter()` and `HasElementsConverter.Instance`. Fixed by making `Converters.HasElements => HasElementsConverter.Instance` (single singleton).
3. Deferred (NOT fixed, TODO added): Flyout 32px gutter width (`MainWindow.axaml:14` ColumnDefinitions) and `RotateTransform 90°` vertical text (`MainWindow.axaml:206`) — left as-is with `TODO(Task 4)` comments per instruction; to be revisited in Task 4 polish.

**Verification:**
- `dotnet build SenderNG.sln` — PASS (0 errors, warnings only pre-existing CS8602/CS8620/CS8619/xUnit1031).
- `dotnet test` — **108/108 PASS** (Duration ~441ms, no regressions).

## Status
DONE (review fix applied)

**Report path:** `C:/Projects/Workshop/SenderNG/.superpowers/sdd/2026-08-22-layout-implementation/task-3-report.md`
