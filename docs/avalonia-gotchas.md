# Avalonia UI gotchas (SenderNG widgets)

Painful, non-obvious Avalonia behaviors we hit while building the widgets, so future
changes don't re-introduce them. Read this before touching any editor panel that is
driven by a single master/detail view (GRBL Settings is the reference implementation).

## 1. Recycled editor controls fire Change events with stale state

**Where it bit us:** GRBL Settings. The detail pane uses **one** shared Control
set per *all* settings — a single `CheckBox` (bool), one `ComboBox` (radio), one
`TextBox` (numeric/text), and an `ItemsControl` of checkboxes (bits). The whole set is
recycled for every `SelectedSetting`.

**The bug:** when you switch from one setting to another, Avalonia re-syncs the recycled
control to the new item's bindings *in stages*. In that window a Change event fires that
still carries the OLD control state but already points at the NEW `SelectedSetting`. For
the ComboBox, `SelectionChanged` surfaced a **stale `SelectedIndex`** (from the previous
setting) targeted at the just-selected setting. Our handler then happily committed it
via `ApplyRadio(staleIndex)`, corrupting the value and marking the setting dirty.

- Classic symptom: a **bitmask** setting (which hides the ComboBox entirely) suddenly
  showed `* changed` and its bit checkboxes flipped, even though nothing was clicked.
- Root cause confirmed via a temporary stack-trace log: the write came from
  `GrblSettingsView.OnDetailSelectionChanged` → `ApplyRadio`, fired during
  `SelectingItemsControl.RaiseSelectionChanged` (a programmatic/recycling change, not a
  user click).

**The fix pattern (used in `OnDetailSelectionChanged`):**
1. **Guard by editor type** — never let an editor's Change event write to a setting
   that doesn't use that editor. The combo may only write when `item.IsRadio`.
2. **Defer the write** to the input queue (`Dispatcher.UIThread.Post(..., DispatcherPriority.Input)`)
   and re-check before committing: the control still targets the same item, the index is
   in range, and it actually differs from the model. After the OneWay bindings settle, a
   recycled/programmatic event becomes a no-op (`idx == item.SelectedIndex`); only a real
   user selection still differs and is committed.

**General rule for this widget family:** never let an editor's `Changed`/`Click` handler
write blindly to `SelectedSetting`. Guard by type, and for list-ish controls defer so the
binding cycle settles. Regression coverage lives in
`tests/SenderNG.Avalonia.Tests/GrblSettingsViewHeadlessTests.cs`
(`Switching_radio_to_bitmask_does_not_corrupt_the_bitmask`).

## 2. Editors are OneWay-bind + explicit-gesture, never TwoWay-writeback

In the same widget, the editors bind their displayed value `Mode=OneWay` and write only
from an explicit user-gesture handler (`Click` for bool/bits, the guarded/deferred
handler for the combo, `TextChanged` for the text box). This is deliberate:

- A `TwoWay` binding made the framework push control **defaults** back into the setting
  during first realization (a combo wrote `-1`, bit checkboxes flipped on), marking
  settings dirty the moment they were selected. OneWay display + explicit writes prevents
  bindings from ever mutating the source.
- For checkboxes specifically we react to `Click`, **not** `IsCheckedChanged` —
  `IsChecked` is a `bool?` that Avalonia can raise `IsCheckedChanged` for during
  realization/template application with a stale value.

If you need a checkbox-like editor, prefer OneWay display + a user-gesture handler (or a
per-item `Command`) over trying to make `IsCheckedChanged`/TwoWay behave.

## Checking in on gotchas

- The headless test project (`tests/SenderNG.Avalonia.Tests`, xUnit v3 +
  Avalonia.Headless.XUnit) can realize the real `GrblSettingsView` and assert UI-level
  behavior without a board. Hidden-at-`Show()` panes do **not** realize in that build —
  select the first item *before* `Show()`, then switch.
- When diagnosing a "dirty right after selection" / "value changed on its own" report,
  suspect a recycled editor first, and capture the write's stack trace rather than guessing.