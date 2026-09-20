# SenderNG Plugin SDK — writing and testing your own module

> **Approved (2026-09-05) and implemented.** This document defines the plugin
> contract. The agreed fixes — generic SDK/module naming (`SenderNG.Modules.Common`,
> `SenderNG.Modules.*`), an SDK that is generic and shares **no UI**
> (converter-specific code lives in an internal, non-shipped `SenderNG.Shared`
> linked-source folder), the extended `IModule` surface (`IsEnabledByDefault`,
> `PanelHint.Dialog`/`None`), optional `IPluginInfo` metadata (About screen),
> core-owned error surfacing via `IErrorReporter` (`Error`/`Status`) and the
> `Id`-collision rule (first wins) — are implemented; statuses tracked in §8.
> The enabled/visible overhaul is out of scope and tracked in the ROADMAP.

## 1. What a plugin is

A plugin (module) is a plain .NET class library that implements
`IModule` (SenderNG.Core) and optionally one or more capability interfaces.
It ships as its own DLL plus localization files, is discovered at startup by a
directory scan of `AppDirectory/Modules/*.dll`, and registers itself into the
app's single DI container. The app never references plugin types at compile
time — it knows only the Core interfaces.

Plugin types today:

- **Converter plugins** — file-format → g-code handlers (Excellon `.drl`,
  HPGL `.plt`). The only plugin surface the app actively dispatches
  (`IGCodeFileHandler`).
- **UI plugins** — modules with a flyout/panel UI, settings leaf, hotkeys
  (Jog, Probing, … in-app; the plotter modules are the separate-assembly
  examples).

## 2. SDK status and the stability rule

The SDK is pre-1.0: nobody outside this repo consumes it yet, so **anything
can change** — no versioning machinery, no compatibility promises. Two rules
apply once the SDK is "alive" (public consumers exist):

- **Versioning** — the SDK assembly gets a real version, and plugins target a
  specific SDK version.
- **Add-only, never change** — existing interfaces, members and semantics
  become frozen; new capability interfaces are added, existing ones are not
  modified.

Until then: no version checks, no deprecation machinery, refactor freely.

## 3. The contract

### 3.1 `IModule` — the only required interface

```csharp
public interface IModule
{
    string Id { get; }                    // unique, stable; settings + routing key
    string Name { get; }                  // display name
    string Description { get; }           // one-liner for App Settings
    int Order { get; }                    // sort order
    PanelHint Panel { get; }              // Left/Center/Right/Flyout/Menu/Bottom/Dialog/None
    bool IsEnabledByDefault { get; }
    bool IsVisibleByDefault { get; }
    bool CanHide { get; }
    IReadOnlyList<HotKey> HotKeys { get; }
    void RegisterServices(IServiceCollection services);   // plugin DI registrations
    object CreateViewModel(IServiceProvider serviceProvider);
    object CreateView(object viewModel);
    (double Width, double Height)? DialogPreferredSize { get; }
}
```

Discovery (`ModuleCatalog.ScanDirectory`) scans every `*.dll` in `Modules/`
into the LoadFrom context, registers every `IModule` implementation with a
public parameterless constructor as a singleton, applies its
`RegisterServices`, and skips broken/non-module DLLs silently — a bad plugin
never takes the app down. A missing `Modules/` directory is a no-op.

**`Id` collisions — first wins among peer plugins, by scan order.** The Id gate
is local to one `Modules/` scan: DLLs load in directory enumeration order
(alphabetical on Windows in practice, unordered elsewhere — treat it as
unspecified), the first `IModule` claiming an `Id` registers, and any later
module with the same `Id` is rejected **silently** — its services, handlers
and UI never register, and nothing is logged or shown (asserted by the
`collision-id` test in `ModuleCatalogTests`). What this means for plugin
authors:

- **Swap-in contract:** replacing one plugin with another is a file swap —
  delete the old DLL (and its `Locale/`), drop in the new one. Id equality is
  irrelevant for a swap. If both DLLs are present at once with the same Id,
  which one loads is decided by scan order — delete the one you don't want.
- **Coexistence** of two plugins of the same kind (two plotters, two
  visualizations) works when their Ids differ. A shared settings section must
  never serve two plugins — that is what the rule protects.
- **Built-in modules are not gated by this rule.** The app registers its
  fixed modules (DRO, Program, Console, Jog, Probing, …) directly, before the
  scan, with no Id check — the scan's Id set does not know them. A plugin
  claiming a built-in Id would therefore load as a duplicate (shared settings
  section, duplicated UI), not be rejected. Never claim an app-owned Id.

### 3.2 `IPluginInfo` — OPTIONAL metadata (new, per SDK review)

Every plugin module can also provide identity metadata beyond the module
type if the Autor wants to display it on About screen. This is a new interface:

```csharp
public interface IPluginInfo
{
    string Version { get; }    // e.g. "1.2.0"
    string Author { get; }     // person or organization
    string License { get; }    // e.g. "BSD-3-Clause", "MIT", "Proprietary"
}
```

`IModule` implementations implement this interface alongside it. The host
surfaces the metadata on the About screen when the plugin implements it, so a
user can see it before trusting the plugin. The platform does **not** police
plugins: it does
not validate licenses, does not rate-limit dialogs, does not sandbox. A
plugin that nags the user with an always-ask dialog is the author's choice;
deciding whether to keep that plugin is the user's.

### 3.3 Capability interfaces (optional)

| Interface | Purpose |
| --- | --- |
| `IModuleSettingsProvider` | Editable settings leaf in App Settings; `Scope` = `Profile` (per-machine, default) or `Global` (app-wide). Numeric/checkbox fields or a full custom view. `AttachServices` receives the host DI once built. A module gets a Modules-tree leaf iff `CanHide` is true or the provider yields a view or at least one field. |
| `ILocalizedModule` | Own locale catalog (`Modules/<id>/Locale/*.json`), keys namespaced `module.<id>.*`, English fallback in `LocalizationKeys`. |
| `IGCodeFileHandler` | Converter plugin: claims file extensions, converts text → g-code through the Open flow. The only interface the app dispatches on. |
| `IPluginInfo` | Identity metadata — optional (see 3.2). |

### 3.4 Error surfacing — owned by Core

Error reporting is **not** each plugin's private UI. Core provides an
`IErrorReporter` service the plugin resolves from the services provider; the
core-side handler is common for all modules:

```csharp
public interface IErrorReporter
{
    /// <summary>Error dialog: "[module name] : [text]" — the loaded program stays untouched.</summary>
    void Error(string text);

    /// <summary>Same text as a status-bar message instead of a dialog.</summary>
    void Status(string text);
}
```

The reporter a module resolves is bound to that module: the host registers
one reporter per module at discovery, keyed by the module's `Id`
(`GetRequiredKeyedService<IErrorReporter>(Id)`), so the common handler knows
which display name to prefix. `Error` shows the dialog, `Status` routes to
the status bar — the plugin just says what happened; the host decides how to
surface it. Today's interim behavior — converter returns `null`, host shows a
generic "Conversion failed" status — remains only as a safety net for
plugins that fail silently.

## 4. Packaging and distribution

```text
OutputDir/
├── SenderNG.Core.dll                    ← app-owned, ships with the app
└── Modules/                             ← plugin drop-in directory
    ├── SenderNG.Modules.Common.dll      ← the SDK assembly, ships with the app
    ├── SenderNG.Modules.Excellon.dll    ← built-in plugins staged on build
    ├── excellon/Locale/en.json          ← plugin-owned locale catalogs
    ├── excellon/Locale/ru.json
    ├── SenderNG.Modules.Hpgl.dll
    └── hpgl/Locale/…
```

**A plugin ships exactly two things: its own `.dll` and its `Locale/` files.**
The SDK assembly (`SenderNG.Modules.Common`) is
part of the app distribution, already present in `Modules/` — plugins
reference it, never bundle it. The host registers each module's
`Modules/<id>/Locale` directory with `LocalizationService` at startup, so
plugin strings resolve through the active locale without any app changes.

**Install** = copy the DLL (and `Locale/`) into `Modules/`. **Remove** =
delete it. No registration, no config, no restart ceremony beyond relaunching
the app. Compile-time rule in this repo: the app references plugin projects
only as build-order links (`ReferenceOutputAssembly="false"`) so plugin types
are never duplicated across the LoadFrom boundary.

## 5. Writing a converter plugin

Converters are the reference plugin type. A converter plugin is just a
module: it implements `IModule` + `IGCodeFileHandler`, plus
`IModuleSettingsProvider` and `ILocalizedModule` when it has parameters or
strings, and ships its own locale catalog. Nothing converter-specific lives
in the SDK: `SenderNG.Modules.Common` carries only generic, UI-free machinery
any plugin type can reuse. The built-in Excellon/HPGL converters pool their
converter-specific code (parameter model, dialog, settings store) in an
internal linked-source folder, `SenderNG.Shared`, that is **not shipped** and
is invisible to the contract; a third-party converter writes its own from
scratch, following the interfaces below.

### 5.1 Project skeleton

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SenderNG.Modules.Common\SenderNG.Modules.Common.csproj" />
  </ItemGroup>
</Project>
```

Plugins that don't build against the repo reference the **shipped**
`SenderNG.Modules.Common.dll` (already present in `Modules/`) instead — same
contract, and they must never copy the SDK into their own package.

The SDK references Core and brings the contracts with it; it ships no UI.
A plugin that needs its own dialog or settings view references Avalonia
itself, pinned to the host's resolved version (12.0.0) — a floating `12.*`
once resolved to a mismatched build and crashed startup with
`TypeLoadException`.

### 5.2 The module class

```csharp
public class GerberModule : IModule, IGCodeFileHandler, IModuleSettingsProvider, ILocalizedModule, IPluginInfo
{
    public string Id => "gerber";
    public string Name => "Gerber";
    public string Description => "Gerber (.gbr) PCB files → g-code.";
    public int Order => 62;

    // Optional metadata — shown on About when implemented.
    public string Version => "1.2.0";
    public string Author => "…";
    public string License => "MIT";

    // Converter plugins have no UI: they are open-flow handlers, not panels.
    public PanelHint Panel => PanelHint.None;
    public bool IsVisibleByDefault => false;
    public bool IsEnabledByDefault => true;
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];
    public void RegisterServices(IServiceCollection services) { }
    public object CreateViewModel(IServiceProvider serviceProvider) => new object();
    public object CreateView(object viewModel) => new object(); // never realized for PanelHint.None

    // ---- IGCodeFileHandler: the only surface the app dispatches on ----
    public string FileType => "Gerber files";                // Open-dialog filter label
    public string FileExtensions => "gbr";                   // lowercase, comma-separated
    public string? FileTypeKey => "module.gerber.fileType";  // localized via your catalog

    public async Task<string?> ConvertAsync(IServiceProvider services, string sourceName, string sourceText)
    {
        // Your flow, from scratch: load stored parameters (§5.3) → show your
        // dialog when needed (IDialogService + IViewFactory) → convert →
        // return g-code. null = cancelled or failed: the host keeps the
        // current program; report the failure via IErrorReporter (§3.4).
        var parameters = await LoadParametersAsync(services, sourceName);
        return Convert(sourceText, parameters);
    }

    // ---- IModuleSettingsProvider: your own leaf in App Settings ----
    public SettingsScope Scope => SettingsScope.Profile;
    public object? CreateSettingsView() => /* your own view */ null;
    public void AttachServices(IServiceProvider services) { }

    // ---- ILocalizedModule: your own catalog, keys module.<id>.* ----
    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.gerber.name"] = "Gerber",
            ["module.gerber.description"] = "Gerber (.gbr) PCB files → g-code.",
            ["module.gerber.fileType"] = "Gerber files",
        };
}
```

The interfaces are the contract; nothing else is assumed:

- **Identity & locale** — `Name`/`Description`/`FileType` resolve through
  `module.<id>.*` keys in your catalog, falling back to your literal strings.
  The host registers `Modules/<id>/Locale` for you; resolve strings via
  `LocalizationService` with an English fallback (no `{Loc}` markup in
  plugins).
- **Open flow** — the host builds the Open-dialog filter from
  `FileType`/`FileExtensions`, dispatches by extension, and calls
  `ConvertAsync`. What happens inside is yours: parameters, dialog,
  persistence, conversion.
- **Settings leaf** — your `CreateSettingsView` is injected into the App
  Settings card; persist under your `Id` in
  `MachineProfile.ModuleSettings` (profile scope) or
  `GlobalSettings.ModuleSettings` (global scope).
- **Dialog hosting** — the host owns the dialog shell only. Register your
  VM→view pair into `IViewFactory` once (idempotent), then show it via
  `IDialogService` resolved from the services provider.

### 5.3 Parameters

Parameters are yours. Define your own model, persist it as JSON under your
module id in the profile's `ModuleSettings`, and show your own dialog
(hosted via `IDialogService` + `IViewFactory`). Two conventions, not
machinery: parameters belong to a machine, so persist **per profile**, never
globally; and "ask on open, silent afterwards" (the ShowDialog flag) is a UX
pattern you implement — an always-ask variant is your choice too. The built-in
converters' `ConverterParameters` + `ConverterSettingsStore` are internal
shared code (`SenderNG.Shared`) — a readable reference, not part of the SDK.

### 5.4 Localization

Your plugin's `Locale/en.json` (required) plus one file per locale, e.g.
`Locale/ru.json`, each with a `strings` object; keys must be namespaced
`module.<id>.*`:

```json
{ "strings": { "module.gerber.name": "Gerber", "module.gerber.fileType": "Gerber files" } }
```

Plugin catalogs are additive — they can never override app strings. English
is the fallback for every locale; a missing translation shows English, never
a raw key. Plugin views must not use the app-only `{Loc}` markup extension —
ViewModel code resolves strings via the injected `LocalizationService` with
an English fallback.

### 5.5 Getting it into the app

Built-in plugins are staged by the app project (build-order reference +
`Link` items for the DLL and `Locale/**`). A third-party plugin skips all of
that: build, copy the DLL into `Modules/`, copy `Locale/` to
`Modules/<id>/Locale/`, run. The Open dialog now advertises
"Gerber files (*.gbr)" and dispatch happens automatically by extension.
The built-in converters share code through `SenderNG.Shared`, but what ships
for them is the same as for any plugin: the DLL and `Locale/`.

## 6. Writing a UI plugin

Every plugin implements `IModule` directly — Excellon is the minimal reference,
Probing a full one; converters are the `PanelHint.None` variant (see §5). The
same rules apply: namespaced locale keys,
own settings leaf via `IModuleSettingsProvider`, services registered in
`RegisterServices`, view/view-model returned as `object` (Core is UI-agnostic;
the Avalonia host casts).

The panel contract is not Flyout-only: `PanelHint` also offers `Dialog` (the
module's UI opens as a dialog — wizards like stepper calibration) and `None`
(no UI at all, e.g. converter plugins). Modules declare whether they start
enabled (`IsEnabledByDefault`); App Settings lets users show/hide and
enable/disable a module independently — what "disabled" gates exactly is the
enabled/visible overhaul tracked in the ROADMAP.

## 7. Testing your plugin

Test the module class directly — no host needed.

- **Conversion logic** — call `Convert(text, parameters)` on the converter
  class and assert on the emitted g-code (see `ConverterTests`:
  identity/locale-key tests, G81 chains, helical interpolation, header
  fidelity).
- **Module contract** — assert `Id`, `FileExtensions`, `FileTypeKey`,
  `ShowCutMode`, `LocalizationKeys` (mirror
  `ConverterTests.Modules_expose_filter_identity_and_locale_keys`).
- **Metadata** — when the module implements `IPluginInfo`, assert
  `Version`/`Author`/`License` are present and non-empty.
- **Settings round-trip** — `ConverterSettingsStore.Load/Save` against a
  `MachineProfile`: defaults for a fresh profile, persistence under the
  module id, corrupt-entry fallback.
- **Discovery** — stage the built plugin DLL into the test output and run
  `ModuleCatalog.ScanDirectory`, then resolve the module from the service
  collection. Match registered modules **by full name**: LoadFrom identity
  differs from compile-time references. This is exactly how the repo tests
  the real Excellon/HPGL DLLs (see the converters plan's "Test
  serialization" note).
- **Manual smoke test** — build, copy DLL + `Locale/` into `Modules/`, open
  a file with your extension, convert, send. Remove the DLL and confirm the
  app doesn't know the format exists.

## 8. Contract summary

| Piece | Interface / type | Where | Status |
| --- | --- | --- | --- |
| Module contract | `IModule` (incl. `IsEnabledByDefault`, `PanelHint.Dialog`/`None`) | SenderNG.Core | implemented |
| Settings leaf | `IModuleSettingsProvider` | SenderNG.Core | implemented |
| Locale catalog | `ILocalizedModule` + `Modules/<id>/Locale` | SenderNG.Core / host | implemented |
| Converter dispatch | `IGCodeFileHandler` | SenderNG.Core | implemented |
| SDK assembly | generic, UI-free machinery only (contracts via Core, settings helpers) | `SenderNG.Modules.Common` | implemented (renamed from `SenderNG.Converters.Common`) |
| Converter sharing (built-in only) | `ConverterModuleBase`, `ConverterParameters`, `ConverterSettingsStore`, shared views | `SenderNG.Shared` — internal, not shipped | implemented (linked-source folder) |
| Dialog hosting | `IViewFactory`, `IDialogService` | SenderNG.Core | implemented |
| Discovery | `ModuleCatalog.ScanDirectory` | SenderNG.Core | implemented |
| Plugin metadata | `IPluginInfo` { Version, Author, License } — optional, About screen | SenderNG.Core + host | implemented |
| Error surfacing | `IErrorReporter` — `Error(text)` → dialog, `Status(text)` → statusbar; bound per module | SenderNG.Core + host | implemented |
| `Id` collision policy | first wins among scan peers (scan order, loser rejected silently); built-ins register before the scan, ungated | `ModuleCatalog` | implemented |

## 9. Review status

All review questions are resolved: SDK/module renames (§4); `IPluginInfo`
optional with About-screen surfacing (§3.2); error surfacing = Core
`IErrorReporter` (`Error(text)` dialog, `Status(text)` statusbar, bound per
module by id — §3.4); `Id` collisions = first wins, second rejected at
discovery (§3.1); `SenderNG.Shared` = linked-source folder, not shipped (§5).

The **enabled vs. visible** question (semantics of `IsEnabledByDefault`, what
"disabled" gates, where it persists) was deliberately left out of the SDK
scope — tracked as a deferred overhaul in `docs/ROADMAP.md`.
