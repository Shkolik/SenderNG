# Localization Architecture

SenderNG localizes its UI through a small, data-driven pipeline: locale strings live in
JSON catalog files, and one shared runtime service resolves keys to the active language.
Everything flows through three entry points depending on *where* the string is produced
(markup, a ViewModel, or a module name). There is no third-party i18n framework — the
mechanism is owned by `SenderNG.Core`.

---

## Data flow at a glance

```text
Locale/*.json (en.json base + ru.json ... overrides)
        │
        ▼
LocalizationService  (SenderNG.Core)
   • loaded once by HostSetup into DI + LocalizationService.Current
   • T(key) / Resolve(key, fallback) / TError / ApplySettingStrings
   • raises Changed when the locale switches
        │
        ├── {loc:Loc key} markup extension   → static XAML text (re-renders live)
        ├── LocText.Resolve / Format         → ViewModel-generated strings
        ├── ModuleNames.Localized(module)    → module display names (plugin-first)
        └── injected LocalizationService     → Core ViewModels (Connect, AppSettings)
```

---

## The catalog files

UI-owned catalogs live in `src/SenderNG/Locale/`:

- `en.json` — the **base** catalog. Required; loaded first.
- `ru.json` — a **per-locale override**; English is the source of truth and never depends
  on a locale file.

Both are copied to `OutputDir/Locale/` on build (see the Avalonia csproj) and loaded from
there at runtime. A locale JSON has three top-level sections:

```jsonc
{
  "locale": "ru",
  "strings": {                // regular app strings, keyed by dotted key
    "app.menu.file": "_Файл",
    "common.close": "Закрыть",
    "module.dro.name": "УЦИ"
  },
  "grblSettings": {           // (optional) per-setting overrides, keyed by setting id
    "23": { "name": "Инвертировать направление хоуминга", "en": "Homing direction invert" }
  },
  "grblSettingGroups": { ... } // (en.json only) group tree / metadata
}
```

### Key conventions

- Keys are **dotted and namespaced**: `app.*` (shell), `common.*` (shared buttons),
  `connect.*`, `about.*`, `module.<id>.*` (per-module), `app.msg.*`
  (code-generated status lines).
- Values may contain format placeholders (`{0}`, `{count}`) — see **And avoid this** below.
- **`en.json` owns every key.** A locale override only *overrides*; it must not invent keys.
  Resolution falls back `locale → English → the key itself`/caller-provided English.

---

## The service: `LocalizationService` (SenderNG.Core)

One app-wide instance is built by `HostSetup.BuildHost`:

- Locale is resolved as `--locale <code>` CLI arg → persisted `GlobalSettings.Language` in `global.json` → `en`.
- Loaded via `LocalizationService.LoadFromDirectory(localeDir, code)`.
- Exposed on DI and assigned to `LocalizationService.Current` — the static that the markup
  extension and `LocText` read, because views can't reach DI.
- Every registered module's `Modules/<id>/Locale/*.json` is merged in through
  `RegisterModuleLocales` so plugins can carry their own strings (plugin-first names).

Key API:

| Member | Purpose |
| --- | --- |
| `T(key)` | Resolve a key: locale → plugin → English → the **key itself** (low-level). |
| `Resolve(key, fallback)` | Same but returns caller's `fallback` instead of a raw key (code-side `{Loc}` counterpart). |
| `SetLocale(code)` | Switch language at runtime; raises `Changed`. |
| `Changed` | Event fired after a locale switch, so live UI re-reads labels. |
| `RegisterModuleLocales(dir)` | Merge a plugin's own `Locale/` JSON into lookup. |
| `TError(code)` / `DecorateError(line)` | Localized grbl `error:<n>` explanations. |
| `ApplySettingStrings(setting)` | Guard-based localization of grbl setting names (see below). |

---

## The three ways to localize a string

### 1. Static XAML text — `{loc:Loc key}`

For text that's decided in markup (menu headers, button labels, tooltips). Add the namespace
and bind:

```xml
<UserControl
    xmlns:loc="clr-namespace:SenderNG.Localization">
    <Button Content="{loc:Loc common.close}"/>
    <MenuItem Header="{loc:Loc app.menu.file}"/>
</UserControl>
```

`LocExtension.ProvideValue` returns a live binding built from `LocSource.HotKey(key)`, an
observable that resolves the key and **re-emits on every `Changed`**, marshalled onto the
UI thread. These strings switch language without any VM involvement.

### 2. ViewModel / code-generated strings — `LocText`

For strings a ViewModel builds at runtime (status lines, messages, errors). Resolve against
`LocalizationService.Current`; when it's null (headless tests, early startup) the English
literal is used so text never leaks a raw key:

```csharp
Status = LocText.Resolve("app.msg.programRewound", "Program rewound");
Status = LocText.Format("module.goto.goingPosition", "Going to position");
var name = LocText.Format("module.probing.heightMapSaved", "Height map saved to {0}", path);
```

In `SenderNG.Localization`. Prefer `Resolve` for plain text and `Format` for
placeholder substitution (`Format` also catches `FormatException`).

### 3. Core ViewModels — injected `LocalizationService.Resolve`

Core VMs (no Avalonia dependency) take an *optional* `LocalizationService` constructor
parameter and call `Resolve` directly when present:

```csharp
public ConnectViewModel(..., LocalizationService? localization = null)
{
    _localization = localization;
    ModeOptions = ...;
    Status = _localization is null ? "Not connected" : _localization.Resolve("app.conn.notConnected", "Not connected");
}
```

The optional parameter keeps existing construction sites and unit tests valid; a null
service falls back to the caller's English defaults.

### Module display names — `ModuleNames.Localized`

Module titles bind a shared helper that resolves `module.<id>.name`, **plugin-first**: for
a 3rd-party module the key exists only in the module's own registered `Locale/` catalog, so
it wins over the base catalog; in-app modules resolve from `en.json`. Falls back to the
module's own `Name` when no catalog has the key:

```csharp
// MainViewModel
private string LocalizedName(IModule m) =>
    _localization is null ? m.Name : ModuleNames.Localized(m, _localization);
```

---

## Live re-localization

When the user picks a new language in App Settings (`SetLocale("ru")`):

1. `LocalizationService.Changed` fires.
2. `{loc:Loc}` bindings from `LocSource.HotKey` re-emit automatically (UI-thread-safe).
3. ViewModels that must rebuild computed text subscribe to `Changed` and re-raise the
   relevant `OnPropertyChanged`. `MainViewModel.OnLocaleChanged` is the reference pattern —
   it re-raises `CenterTabs`, `RightModuleViews`, `LeftModules`, `FlyoutModules`,
   `SelectedModuleName`, `DroHeader`, `Title`, `MachineStateDisplay`,
   and `Status`, all marshalled through `UiThread.Post`:

```csharp
private void OnLocaleChanged(object? sender, EventArgs e) =>
    UiThread.Post(() =>
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Status));
        // ...every locale-dependent computed property
    });
```

A view model that subscribes to `Changed` should implement `IDisposable`, unsubscribe in
`Dispose`, and be certain it's the single owner of its strings. Static XAML strings need no
work at all.

---

## grbl setting localization and the guard

grbl setting *names* are localized too, but carefully. The controller (grblHAL) can report
its own setting metadata; a translation is applied only when it's safe:

- Each override entry carries `en` — the canonical English name the translation was
  authored against.
- `ApplySettingStrings` applies the locale's `name`/`description` **only if** that
  `en` name (case-insensitive) matches the setting's own English name, or either side has
  none (classic grbl, whose ids are spec-stable).
- On any mismatch the controller's own string is kept, rather than misapplying a
  translation to a differently-meaning id.

When a locale is switched, overrides are rebuilt and re-applied on the next read.

---

## Adding or improving a translation

1. **Add the key to `en.json` first** (the base), with the English text as the fallback
   contract.
2. **Mirror the key in the locale override(s)** (`ru.json`, …) with the translated value.
3. Keep values **keys = one line** where possible; embedded `\n` is fine (some probing
   instructions rely on it).
4. Run the catalog test — `Shell_keys_resolve_in_english_and_russian` asserts every
   shell key resolves in both `en` and `ru` without leaking a raw key:

   ```bash
   dotnet test tests/SenderNG.Core.Tests --filter FullyQualifiedName~Shell_keys
   ```

### Adding a whole new language

1. Copy `en.json` to `Locale/<code>.json` and translate `strings` (+ `grblSettings`).
2. The language appears automatically in the App Settings picker via
   `LocalizationService.AvailableLocales` (scans `OutputDir/Locale/*.json`, English first).
3. Update the catalog test to also resolve in the new code if you want it part of CI.

---

## Plugin (3rd-party module) localization

A plugin ships its own `Locale/en.json` + `Locale/<lang>.json` under its module output dir
(`Modules/<id>/Locale/`). At host startup each module's directory is registered via
`RegisterModuleLocales`. The plugin's strings:

- Use **its own namespaced keys** (`module.<id>.*`) — base catalogs take precedence, so a
  plugin may not override app strings.
- Resolve through `T()` for the active locale and contribute to `AvailableLocales`.
- Feed module names through `ModuleNames.Localized` (plugin-first).

Example plugin catalog:

```jsonc
// src/SenderNG.Modules.Excellon/Locale/ru.json
{
  "strings": {
    "module.excellon.name": "Excellon",
    "module.excellon.description": "Файлы сверловки Excellon (.drl/.xln) → g-code.",
    "module.excellon.fileType": "Файлы Excellon"
  }
}
```

---

## Testing

- **Core tests** (`tests/SenderNG.Core.Tests`): plain unit tests against a
  `LocalizationService` built from the real on-disk `Locale/` dir. Tests that call
  `SetLocale` mutate only a **private instance** — never `TestLocalization.Locale` (the
  shared static is read by many classes running in parallel, so it must stay unmutated).
- **Headless tests** (`tests/SenderNG.Tests`): realize real views and assert
  `{loc:Loc}` / live re-localization. They set
  `LocalizationService.Current = fixtureLocale`, drive `SetLocale("ru")` / `SetLocale("en")`,
  pump the UI dispatcher, and restore `Current` in a `finally`:

  ```csharp
  Dispatcher.UIThread.RunJobs();
  AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
  await Task.Yield();
  ```

- Reference tests: `ShellLocalizationHeadlessTests`, `AboutDialogHeadlessTests`,
  `LocSourceTests`, `LocExtensionHeadlessTests`.
  
