# Theming — light & dark

## 1. Overview

`src/SenderNG/Themes/SenderTheme.axaml` holds the whole palette as semantic
`Sender.*` brush tokens in a `ResourceDictionary.ThemeDictionaries` block with a
`Light` and a `Dark` entry, merged into `Application.Resources` from
`App.axaml`. Views never hold colors of their own — they reference tokens via
`{DynamicResource Sender.*}`. Theme switching is live: changing
`RequestedThemeVariant` (via `ThemeVariants.Apply` / `ThemePreference`) swaps
the dictionary with no restart. `ThemePreference` stores the user's choice
(`System` / `Light` / `Dark`); `System` follows the OS.

Light values are today's original literals, frozen byte-for-byte. Dark is the
approved "Soft graphite" palette. `SenderTheme.axaml` is the **single source
of truth** — module DLLs get no per-project theme dictionaries.

## 2. Rules

- **Never hardcode a color in view AXAML.** No `#rgb`/`#rrggbb`/`#aarrggbb`
  and no named color (`White`, `Black`, `Red`, …) may appear in any
  `src/**/*.axaml` except `SenderTheme.axaml` itself.
- **`Transparent` is the only allowed literal.**
- **New colors become `Sender.*` tokens** added to *both* the Light and Dark
  dictionaries in `SenderTheme.axaml` (the only theme dictionary).
- All view color references use `{DynamicResource Sender.*}` (or
  `Setter Value="{DynamicResource Sender.*}"`) — never `StaticResource`, so
  the theme switches live.
- `ThemeHygieneTests` enforces all of this: a comment-stripped scan of every
  view AXAML file (the theme file itself is excluded). A stray literal fails
  the build.
- Semantic C# colors drawn outside XAML (machine-state chip brushes in
  `MainViewModel`, toolpath pens, axis/plane labels, HUD chips…) stay
  hardcoded by design — they are not theme-dependent chrome.

## 3. Token reference

All values are `SolidColorBrush` entries; Light/Dark columns match
`SenderTheme.axaml` exactly (the file is authoritative — this table is for
convenience).

### Surfaces

| Token | Light | Dark |
| --- | --- | --- |
| `Sender.Bg.Window` | `#f6f6f6` | `#262626` |
| `Sender.Bg.Card` | `#fafafa` | `#2f2f2f` |
| `Sender.Bg.Panel` | `#ffffff` | `#2f2f2f` |
| `Sender.Bg.Chrome` | `#e8e8e8` | `#1f1f1f` |
| `Sender.Bg.Sidebar` | `#e0e0e0` | `#242424` |
| `Sender.Bg.Button` | `#eeeeee` | `#3a3a3a` |
| `Sender.Bg.Button.Checked` | `#bdbdbd` | `#4a4a4a` |
| `Sender.Bg.Chip` | `#e4e4e4` | `#3a3a3a` |
| `Sender.Bg.Chip.Hover` | `#d8d8d8` | `#454545` |
| `Sender.Bg.Selected` | `#dddddd` | `#3a3a3a` |

### Text

| Token | Light | Dark |
| --- | --- | --- |
| `Sender.Text.Primary` | `#333333` | `#e4e4e4` |
| `Sender.Text.Strong` | `#222222` | `#f0f0f0` |
| `Sender.Text.OnButton` | `#000000` | `#e4e4e4` |
| `Sender.Text.OnAccent` | `#ffffff` | `#ffffff` |
| `Sender.Text.Secondary` | `#666666` | `#aaaaaa` |
| `Sender.Text.Caption` | `#777777` | `#999999` |
| `Sender.Text.Muted` | `#555555` | `#8c8c8c` |
| `Sender.Text.Muted.Alt` | `#888888` | `#8c8c8c` |
| `Sender.Text.Disabled` | `#999999` | `#666666` |
| `Sender.Text.Link` | `#0066cc` | `#6cb6ff` |
| `Sender.Text.Error` | `#aa3333` | `#f28b82` |
| `Sender.Text.Error.Strong` | `#cc0000` | `#ff7a7a` |
| `Sender.Text.Error.Alt` | `#ff0000` | `#ff6b6b` |
| `Sender.Text.Warn` | `#664d00` | `#ffd77a` |
| `Sender.Text.Warn.Strong` | `#c03030` | `#ffb86b` |
| `Sender.Text.Success` | `#008800` | `#7ed67e` |

### Borders / dividers

| Token | Light | Dark |
| --- | --- | --- |
| `Sender.Border` | `#aaaaaa` | `#3a3a3a` |
| `Sender.Border.Soft` | `#e5e5e5` | `#333333` |
| `Sender.Border.Soft.Alt` | `#e6e6e6` | `#333333` |
| `Sender.Border.Input` | `#999999` | `#555555` |
| `Sender.Border.Button.Checked` | `#666666` | `#4a4a4a` |
| `Sender.Border.Selected` | `#666666` | `#555555` |
| `Sender.Border.Chip` | `#bbbbbb` | `#4a4a4a` |
| `Sender.Divider` | `#dddddd` | `#3a3a3a` |
| `Sender.Divider.Strong` | `#cccccc` | `#3a3a3a` |
| `Sender.Divider.Strong.Alt` | `#d0d0d0` | `#3a3a3a` |

### Icons

| Token | Light | Dark |
| --- | --- | --- |
| `Sender.Icon.Checked` | `#111111` | `#f0f0f0` |
| `Sender.Icon.Hover` | `#444444` | `#d0d0d0` |
| `Sender.Icon.Action` | `#1c5f8a` | `#6cb6ff` |
| `Sender.Icon.Action.Hover` | `#0d3d5c` | `#8ccdff` |

### Accent / warn / danger / state

| Token | Light | Dark |
| --- | --- | --- |
| `Sender.Accent` | `#e04545` | `#e05555` |
| `Sender.Accent.Border` | `#b00000` | `#a03a3a` |
| `Sender.Accent.Border.Alt` | `#b03535` | `#a03a3a` |
| `Sender.Danger.Bg` | `#aa3333` | `#b04a4a` |
| `Sender.Danger.Border` | `#7a2626` | `#8a3a3a` |
| `Sender.Warn.Bg` | `#fff8e1` | `#3a3320` |
| `Sender.Warn.Border` | `#e0c060` | `#8a7a40` |
| `Sender.Warn.Text` | `#5a4a00` | `#ffd77a` |
| `Sender.State.Border` | `#88aa88` | `#88aa88` |
| `Sender.Signal.Dot` | `#ff0000` | `#ff0000` |

### Plotter / canvas

| Token | Light | Dark |
| --- | --- | --- |
| `Sender.Canvas.Bg` | `#ffffff` | `#2a2a2e` |
| `Sender.Plot.Status` | `#b03030` | `#b03030` |

### Overlays

| Token | Light | Dark |
| --- | --- | --- |
| `Sender.Overlay.Busy` | `#99ffffff` | `#99ffffff` |
| `Sender.Overlay.Pressed` | `#22000000` | `#22000000` |

## 4. Fluent bridge

Avalonia's Fluent theme draws its own dark chrome for inputs, menus and
popups; without intervention those controls stay light in the Dark variant
even though the surrounding shell darkens. `SenderTheme.axaml` therefore also
pins the FluentTheme *resource keys* below (in both dictionaries — Light
values are frozen from FluentTheme's resolved brushes so the light theme stays
byte-identical, Dark re-colours to Soft graphite). Resource keys only: Fluent
control templates are never edited.

Five bridge entries are `Color`-typed rather than brushes (Fluent's Expander
template consumes them via `DynamicResource`); four use `Color` + `Opacity`
instead of a hex value. Accent-derived states — CheckBox/RadioButton glyphs,
ToggleButton checked, text selection highlight, focus borders, list/tab
selection — are deliberately not bridged: they follow the system accent in
both themes (surfaces, text and borders only are pinned).

| Key | Light | Dark |
| --- | --- | --- |
| `TextControlBackground` | `#66ffffff` | `#3a3a3a` |
| `TextControlBackgroundPointerOver` | `#99ffffff` | `#454545` |
| `TextControlBackgroundFocused` | `White` | `#3a3a3a` |
| `TextControlBackgroundDisabled` | `#33000000` | `#2f2f2f` |
| `TextControlForeground` | `Black` | `#e4e4e4` |
| `TextControlForegroundPointerOver` | `Black` | `#e4e4e4` |
| `TextControlForegroundFocused` | `Black` | `#e4e4e4` |
| `TextControlForegroundDisabled` | `#ff7a7a7a` | `#8a8a8a` |
| `TextControlBorderBrush` | `#99000000` | `#555555` |
| `TextControlBorderBrushPointerOver` | `#cc000000` | `#666666` |
| `TextControlBorderBrushDisabled` | `#33000000` | `#3a3a3a` |
| `ComboBoxBackground` | `#66ffffff` | `#3a3a3a` |
| `ComboBoxBackgroundPointerOver` | `#99ffffff` | `#454545` |
| `ComboBoxBackgroundPressed` | `#33000000` | `#454545` |
| `ComboBoxBackgroundDisabled` | `#33000000` | `#2f2f2f` |
| `ComboBoxForeground` | `Black` | `#e4e4e4` |
| `ComboBoxForegroundDisabled` | `#66000000` | `#8a8a8a` |
| `ComboBoxBorderBrush` | `#99000000` | `#555555` |
| `ComboBoxBorderBrushPointerOver` | `#cc000000` | `#666666` |
| `ComboBoxBorderBrushDisabled` | `#33000000` | `#3a3a3a` |
| `ComboBoxDropDownBackground` | `#fff2f2f2` | `#2f2f2f` |
| `ComboBoxDropDownBorderBrush`¹ | `Color=Black, Opacity=0.14` | `#3a3a3a` |
| `MenuFlyoutPresenterBackground` | `#fff2f2f2` | `#2f2f2f` |
| `MenuFlyoutPresenterBorderBrush`¹ | `Color=Black, Opacity=0.14` | `#3a3a3a` |
| `FlyoutPresenterBackground` | `#fff2f2f2` | `#2f2f2f` |
| `ExpanderHeaderBackground`² | `#CCFFFFFF` | `Transparent` |
| `ExpanderHeaderBackgroundPointerOver`² | `#CCFFFFFF` | `#3a3a3a` |
| `ExpanderHeaderForeground`² | `#FF000000` | `#e4e4e4` |
| `ExpanderHeaderForegroundPointerOver`² | `#FF000000` | `#e4e4e4` |
| `ExpanderContentBackground`² | `#FFF2F2F2` | `#2f2f2f` |
| `ToggleButtonBackground` | `#33000000` | `#3a3a3a` |
| `ToggleButtonBackgroundPointerOver`¹ | `Color=Black, Opacity=0.1` | `#454545` |
| `ToggleButtonForeground` | `Black` | `#e4e4e4` |
| `ToggleButtonBorderBrush` | `Transparent` | `#555555` |
| `ToolTipBackground` | `#fff2f2f2` | `#1f1f1f` |
| `ToolTipForeground` | `Black` | `#e4e4e4` |
| `ToolTipBorderBrush`¹ | `Color=Black, Opacity=0.14` | `#3a3a3a` |
| `ScrollBarThumbFillPointerOver` | `#66000000` | `#777777` |
| `ScrollBarThumbFillPressed` | `#99000000` | `#999999` |

¹ `SolidColorBrush` defined as `Color` + `Opacity` attributes, not a hex value.
² `Color`-typed resource (not a brush) — Fluent's Expander template reads these
via `DynamicResource`.

## 5. Consumers in C#

XAML-free code can't use `{DynamicResource}`, so:

- **`ThemeResources.Find(key)`** (`src/SenderNG/Themes/ThemeResources.cs`) —
  resolves the token's color for the current variant out of
  `Application.Resources`. Used by the dialog services (`AvaloniaDialogService`,
  `AvaloniaFileDialogService`) so dialog content painted from code follows the
  theme.
- **`PlotterCanvas.Brush`** (`src/SenderNG.Modules.Plotter/PlotterCanvas.cs`) —
  Skia plotter canvas background, resolved from `Sender.Canvas.Bg` at draw
  time.
- **`GlCanvasBackground.Rgba`**
  (`src/SenderNG.Modules.Plotter.GL/GlCanvasBackground.cs`) — GL clear-color
  bytes from `Sender.Canvas.Bg`.
- **`HeightMapViewerView.BackgroundRgbaFor`**
  (`src/SenderNG.Modules.HeightMapViewer/HeightMapViewerView.axaml.cs`) — GL
  clear-color bytes for the height-map viewport; same frozen-light /
  `Sender.Canvas.Bg`-dark contract as `GlCanvasBackground.Rgba`, kept local
  because module assemblies don't reference each other.
- **`ActualThemeVariantChanged`** — `PlotterGlView` and `HeightMapViewerView`
  re-resolve canvas colors when the variant flips so redrawn frames pick up the
  new palette without a restart; the Skia path re-resolves at draw time instead.

## 6. Tests

- `tests/SenderNG.Tests/ThemeDictionaryTests.cs` — every token present with a
  non-empty value in both variants.
- `tests/SenderNG.Tests/ThemeHygieneTests.cs` (with `ColorLiteralScanner.cs`) —
  zero color literals in view AXAML outside the theme file (the only
  exclusion; there is no allowlist); `Transparent` allowed, comments stripped
  first.
- `tests/SenderNG.Tests/FluentBridgeTests.cs` — bridge keys resolve in both
  variants.
- `tests/SenderNG.Tests/ThemeVariantsTests.cs` — `ThemePreference` /
  `ToVariant` / `Apply` / `Applier` behavior.
- `tests/SenderNG.Tests/ThemeShellHeadlessTests.cs` — the shell (main window)
  restyles on variant change; live switching works headless.
- `tests/SenderNG.Tests/ThemeConsumerTests.cs` — `ThemeResources.Find`,
  `PlotterCanvas.Brush`, `GlCanvasBackground.Rgba` resolve per variant.
- `tests/SenderNG.Tests/GlCanvasBackgroundTests.cs` — GL clear-color byte
  mapping.
- `tests/SenderNG.Tests/AppearanceLeafHeadlessTests.cs` — the App Settings
  Appearance leaf exposes the theme choice end-to-end.
- `tests/SenderNG.Core.Tests/ThemePreferenceTests.cs` — `System`/`Light`/
  `Dark` vocabulary + `Normalize`.
- `tests/SenderNG.Core.Tests/AppearanceLeafTests.cs` — appearance settings
  leaf logic in Core.

## 7. Known limits / deferred

- **Fluent chrome keys that don't exist in Avalonia 12** — e.g. there is no
  `ListBoxBackground` (and similar ListBox/other chrome keys); those controls
  keep FluentTheme's own dark values. Not a palette gap we can bridge — the
  keys simply aren't in this Avalonia version. Any bridge candidate that did
  not resolve at implementation time falls in the same bucket: documented
  here, no code impact.
- **Dialogs resolve the theme once at construction** —
  `AvaloniaDialogService` / `AvaloniaFileDialogService` capture the current
  variant when creating a dialog; a dialog that is already open when the user
  switches theme restyles its content (DynamicResource) but keeps its original
  background. Switching before opening is unaffected.
- **Manual grid-on-charcoal check** (plan checklist) — both plotters need a
  real desktop run confirming grid + toolpath readability on the `#2a2a2e`
  canvas and that the `Sender.Plot.Status` `#b03030` pill still reads on
  dark. Automated headless tests cover resolution, not pixels.
- **App icon and machine-topology PNGs** (plan checklist) — light-gray image
  backing needs a visual review on dark surfaces (asset decision, deferred to
  the maintainer).
- **README dark-mode screenshot** — deferred to the maintainer.
