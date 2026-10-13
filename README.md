# SenderNG

**Modern cross-platform (Windows + Linux + Linux ARM64) grblHAL gcode sender based on [ioSender](https://github.com/terjeio/ioSender) by Io Engineering (Terje Io).**

[![CI](https://github.com/Shkolik/SenderNG/actions/workflows/ci.yml/badge.svg)](https://github.com/Shkolik/SenderNG/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Shkolik/SenderNG?include_prereleases)](https://github.com/Shkolik/SenderNG/releases)
[![License](https://img.shields.io/github/license/Shkolik/SenderNG)](LICENSE)

![SenderNG](/docs/images/main_screen.png)
![SenderNG](/docs/images/plot_jog.png)

## Table of contents

- [About](#about)
- [Features](#features)
- [App settings](#app-settings)
- [Supported controllers and limitations](#supported-controllers-and-limitations)
- [Plugins](#plugins)
- [System requirements](#system-requirements)
- [Origin and License](#origin-and-license)
- [Development](#development)
- [Documentation](#documentation)
- [Running from release assets](#running-from-release-assets)
  - [Windows](#windows)
  - [Linux](#linux)
- [Linux release build (WSL2)](#linux-release-build-wsl2)
- [Linux arm64 (Raspberry Pi)](#linux-arm64-raspberry-pi)
- [Contributing](#contributing)
- [Acknowledgements](#acknowledgements)

## About

SenderNG is a focused fork of ioSender. The main differences are cross-platform support, 2-plane machine support (hotwire CNC foam cutters) and a Demo mode that lets users test G-code without hardware. The sender supports as many axes as the controller reports — the axis count comes from the firmware, not the app; producing multi-axis G-code is CAM's scope. The base app contains only core functionality and can be extended with plugins (see [Plugins](#plugins)).

## Features

- Cross-platform: Windows + Linux x64 + Linux ARM64. ARM64 is tested on a Raspberry Pi 4B; x64 is tested on Ubuntu 22.04 and Mint 21.3.
- 2-plane machine support (hotwire CNC foam cutters)

![Foam mode](/docs/images/foam_plot_jog.png)

- Connect: three-tab connect dialog — Serial (port list, refresh, baud), Network (TCP, for grblHAL Ethernet boards) and Demo (always available, with a disclaimer). The app starts disconnected and shows an obvious "NOT CONNECTED" state until you pick one.

![Connect dialog](/docs/images/connect.png)

- Demo mode (test G-code without hardware)

![Connect dialog - demo mode](/docs/images/connect_demo.png)

- Localization (en/ru/fr/es/de). English and Russian are proofread; the other languages are automatic translations.

![Localization settings](/docs/images/localization.png)

- Jogging with a hold-to-jog continuous step (hold to move, release to stop)

![Jog widget](/docs/images/jog.png)

- Probing (tool-length, edge, center, rotation and height-map cycles)

![Probing widget](/docs/images/probing.png)

- Plotter (2D/Isometric). No orbiting, but works everywhere.

![Plotter widget](/docs/images/plot_iso.png)

- Plotter 3D. Supports orbiting and snap views, but requires OpenGL 3.1+ and may be resource-hungry (tested on an Intel Compute Stick — may be slow, RPi 4B works just fine)

![Plotter 3D widget](/docs/images/plot_3d.png)

- Height map 3D. An interactive surface of the probed height map — orbit/pan/zoom, control-point markers, X/Y/Z axis arrows, and a live "points / max ΔZ" readout — opened from the Probing widget's **3D View** action.

![Height map 3D viewer](/docs/images/height_map_preview.png)

- Macros

![Macros widget](/docs/images/macros.png)

- Excellon & HPGL converters
- Outline
- Coolant/mist control
- MDI
- Offsets and tool table support (grblHAL)

![Offsets widget](/docs/images/offsets.png)

- G-code transforms: Rotate, ArcsToLines, Compress (File → Transform)
- Keyboard shortcuts with a fully remappable keymap (remap screen in App Settings)

![Hotkeys settings](/docs/images/hotkeys.png)

- Cycle bar: the single run-control surface (start/hold/resume/stop/rewind)

![Cycle bar](/docs/images/cycle.png)

- Cutting widget: live feed and speed readout, feed/rapids/speed override controls that mirror the controller, and spindle control

![Cutting widget](/docs/images/cutting.png)

- Stepper calibration wizard

![Stepper calibration](/docs/images/calibration.png)

- Automatic controller type and features detection ($I handshake at connect)
- Instant settings update
- Machine profiles and settings presets. Settings are saved per machine profile, so switching between different machines is as easy as selecting a profile.

![Profiles settings](/docs/images/profiles.png)

> **Note:** Not affiliated with or endorsed by Io Engineering / upstream ioSender.

> **Status:** Early stages. Cross-platform Avalonia UI + .NET 9+; demo/offline mode, serial + TCP connect, DRO/Jog/Probing, Plotter widgets, localization (en/ru/fr/es/de).

## App settings

Everything is configured from App Settings, scoped to the active machine profile (one file per machine plus a `global.json`, with a profile manager inside the settings). Almost any widget can be hidden from there — every module has a visibility toggle (the built-ins: DRO, Jog, Console, Program, Plotter, Coolant, plus the tabs installed by plugins), so the UI shows only what your machine needs. Hotkey remapping, converter parameters and per-module options live there too.

![App settings](/docs/images/settings.png)

### Backing up and restoring settings

Global → **Backup and restore** writes one `*.ngsettings.zip` archive holding the whole settings
directory (`global.json`, every `profiles/<id>.json` and each module companion) in the same internal
layout as on disk — so it can also be restored by hand: unzip it over the settings directory with
SenderNG closed. Restoring replaces those files in one step and then asks for a restart, because the
running session still holds the pre-restore values; until you restart, SenderNG refuses to save settings
so it cannot overwrite what you just restored (a backup is still allowed while a restart is pending).

The **Back up** / **Restore** buttons in the Grbl settings dialog do the same for the controller's own
`$$` settings, as a JSON file carrying the controller's identity and a fingerprint of its settings.
Restoring shows a confirm strip first and writes only the settings that differ; a value the controller's
own metadata rejects is listed with its reason instead of being sent.

## Supported controllers and limitations

SenderNG speaks the grbl / grblHAL protocol:

- **grblHAL** — the primary target. Full capability detection via the connect-time `$I` handshake: axis count and letters (`[AXS:]`), tool count, options, signals, settings catalog (`$ES`/`$EG`), the G10 L1 tool table, and extended coordinate systems (G59.2/G59.3).
- **Classic grbl 1.1 and forks** (e.g. the mega5x 1.2i build) — verified against real boards. Detection degrades gracefully: no `[AXS:]` → 3-axis default, settings fall back to the embedded English catalog, no G10 L1 tool table, coordinate systems limited to G54..G59, and mist (M7) support is assumed rather than reported.
- **Transports** — serial (COM ports, `/dev/ttyUSB*`, `/dev/ttyACM*`), plain TCP (grblHAL Ethernet boards), or Demo mode.

Limitations:

- TCP only — no telnet option negotiation and no WebSocket transport yet.
- The app shows as many axes as the firmware reports; producing multi-axis G-code is CAM's scope.
- No machine kinematics model yet — jog layout is driven by machine profiles (Mill / Foam presets), not by an axis-role map.
- Camera support is deferred indefinitely (if ever, as a 3rd-party plugin).
- The Plot 3D tab ships on every platform but is hidden by default; on ARM64 it renders via Mesa.

## Plugins

The base app ships only core functionality; everything else is a plugin — a separate assembly discovered by scanning `Modules/*.dll` at startup. No DLL, no feature: the core bundle runs fine without any of them, and installing one is a straight copy of the plugin zip's contents into the bundle's `Modules/` folder (see [Running from release assets](#running-from-release-assets)). This repository includes:

- Probing — tool-length, edge, center, rotation and height-map probing cycles
- Height map 3D viewer — interactive 3D surface of the probed height map (opened from the Probing widget)
- Plotter (2D/Isometric)
- Plotter 3D (OpenGL orbit view)
- Excellon converter
- HPGL converter

Lathe, DragKnife and other heavy modules are planned. To write your own plugin, see [docs/plugin-development.md](docs/plugin-development.md).

## System requirements

| | Minimum |
|---|---|
| Windows | Windows 10/11 x64 — the bundle is self-contained, no .NET install needed |
| Linux x64 | Distro with X11; `libx11-6 libice6 libsm6 libfontconfig1` (Debian/Ubuntu package names) |
| Linux ARM64 | 64-bit Raspberry Pi OS on a Pi 4B — a 32-bit userland cannot run a `linux-arm64` bundle |
| Serial | membership in the `dialout` group on Linux (see [Linux](#linux)) |
| Controller | grblHAL or grbl 1.1 over serial or plain TCP — see [Supported controllers and limitations](#supported-controllers-and-limitations) |

## Origin and License

- Based on `ioSender` `2.0.48` (`terjeio/ioSender`) — see [original BSD 3-Clause](https://github.com/terjeio/ioSender/blob/master/LICENSE).
- **License:** BSD 3-Clause — [LICENSE](/LICENSE) retains `Copyright (c) 2019-2023, Io Engineering (Terje Io)` and adds `Copyright (c) 2024-2026, Andrew Shkolik`. See `LICENSE` for full text.
- Avalonia UI (`AvaloniaUI/Avalonia`) is MIT — compatible with BSD.

> **No warranties:** THE SOFTWARE IS PROVIDED "AS IS", without warranty of any kind, express or implied. See [LICENSE](/LICENSE) for the full BSD 3-Clause disclaimer.

## Development

```bash
# .NET 9 SDK + Avalonia 12 required (global.json pins 9.0.100)
dotnet build
dotnet test SenderNG.sln
dotnet run --project src/SenderNG
```

## Documentation

- [Architecture](docs/architecture.md) — solution layout, engine and shell
- [Localization](docs/localization.md) — adding and editing locale catalogs
- [arm64 cookbook](docs/arm64-cookbook.md) — build, install and verify on a Raspberry Pi

## Running from release assets

**Automated weekly builds:** every Monday 06:00 UTC the **Release** workflow
(`.github/workflows/release.yml`) builds win64, linux-x64 and linux-arm64 with
all plugins and publishes a prerelease to the
[Releases page](https://github.com/Shkolik/SenderNG/releases) — unless nothing
changed since the previous release, in which case it skips. Formal releases use
the same workflow: bump `<Version>` in `src/SenderNG/SenderNG.csproj`, then run
the workflow manually from the Actions tab with *stable* checked.

Release zips are produced per platform by `tools/ReleaseManager`. On Windows,
build one with:

```bash
dotnet run --project tools/ReleaseManager --include-plugins
```

`tools/ReleaseManager/README.md` has the per-platform runbooks and the manual
fallback. (To build the **Linux** zip from a Windows host, use
`tools/build-linux-release.ps1` — see "Linux release build (WSL2)" below; it
writes `output\linux` and never reads, writes or deletes `output\win64`.)
A release folder (`output/win64/` on Windows, `output/linux/` or
`output/linux-arm64/` on Linux) looks like this. The example is an x64
release (arm64 ships the same plugin set — the Plot 3D tab runs on Mesa and is
hidden by default):

```
SenderNG-<ver>-<suffix>.zip                  ← core bundle: the whole app
SenderNG-plugin-excellon-<ver>-<suffix>.zip
SenderNG-plugin-hpgl-<ver>-<suffix>.zip
SenderNG-plugin-plotter-<ver>-<suffix>.zip
SenderNG-plugin-plotter-gl-<ver>-<suffix>.zip
SenderNG-plugin-probing-<ver>-<suffix>.zip
SenderNG-plugin-heightmap-viewer-<ver>-<suffix>.zip
SHA256SUMS.txt                              ← <sha256> <relative path> per file
```

`<suffix>` is `win64` on Windows, `linux-x64` or `linux-arm64` on Linux.

**Core bundle zip** — the complete self-contained app: `SenderNG.exe` /
`SenderNG` runner, all .NET + platform natives, locale catalogs, and a
`Modules/` folder holding the SDK assemblies — `SenderNG.Modules.Common.dll`,
`SenderNG.Modules.Common.Render.dll` and the Silk.NET `Core`/`Maths`/`OpenGL`
DLLs. Extract it anywhere and run; no .NET install needed.

**Plugin zips** — one per optional module (`excellon`, `hpgl`,
`plotter`, `plotter-gl`, `probing`, `heightmap-viewer`). Each contains the
module's own assembly (`SenderNG.Modules.<X>.dll`) plus an `<id>/` support
folder (its locale catalog). The render SDK and Silk.NET DLLs ship flat in the
core bundle's `Modules/` (Model A), so `plotter-gl` no longer bundles its own
private Silk.NET copy. The app discovers modules by scanning `Modules/*.dll` at
startup, so installing a module is a straight copy of the zip's contents into
the extracted bundle's `Modules/` folder: the DLL lands flat next to the SDK,
`<id>/` becomes `Modules/<id>/`. Skip the ones you don't want — the core bundle
runs fine without them. `probing` is the in-tree first-party module
(`src/SenderNG.Modules.Probing`) holding the Probing flyout — tool-length,
edge, center, rotation and height-map probing cycles; `heightmap-viewer`
(`src/SenderNG.Modules.HeightMapViewer`) is the in-tree 3D height-map dialog.

Verify the downloads before installing: on Windows compare `Get-FileHash`
against `SHA256SUMS.txt`; on Linux run `cd <platform dir> && sha256sum -c
SHA256SUMS.txt`.

### Windows

1. Requires Windows 10/11 x64. Nothing else — the bundle is self-contained.
2. Unzip `SenderNG-<ver>-win64.zip` (Explorer: right-click → Extract All).
   You get a `SenderNG` folder containing `SenderNG.exe`.
3. Optional modules: unzip each plugin zip and copy its contents — the
   `SenderNG.Modules.<X>.dll` plus the `<id>/` folder (skip `README.txt`) —
   into `SenderNG\Modules\`.
4. Run `SenderNG\SenderNG.exe` → Connect → pick the machine's serial/TCP port.

### Linux

1. Grant yourself access to the serial port. Linux gives `/dev/ttyUSB*` and
   `/dev/ttyACM*` to the `dialout` group, mode `0660` — so your account must be
   in that group or the kernel refuses the port (see
   [Serial port permission errors](#serial-port-permission-errors) below):
   ```
   sudo usermod -aG dialout $USER
   ```
   Log out and back in afterwards; an already-running desktop session keeps the
   old group set.
2. Requires Avalonia's X11 system libraries. On Debian/Ubuntu:
   ```
   sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1
   ```
3. Unzip `SenderNG-<ver>-linux-x64.zip` and restore the runner's execute bit
   (zips don't always carry it):
   ```
   unzip SenderNG-<ver>-linux-x64.zip
   chmod +x SenderNG/SenderNG SenderNG/install-desktop.sh
   ```
4. Optional modules: same copy step as Windows — plugin contents into
   `SenderNG/Modules/`.
5. Run `./SenderNG/SenderNG` → Connect → open the machine's port
   (`/dev/ttyUSB*` / `/dev/ttyACM*` for serial, or TCP).
6. Optional: add a desktop-menu entry. This installs `SenderNG.desktop` into
   `~/.local/share/applications/` and the icon into
   `~/.local/share/icons/hicolor/512x512/apps/`, pointing at the absolute path
   of this bundle. Move the bundle somewhere permanent first — moving it
   afterwards orphans the entry.

   ```
   cd SenderNG
   ./install-desktop.sh
   ```

   Undo it with `./install-desktop.sh --uninstall`.

#### Serial port permission errors

A port that shows up in the dropdown but won't connect usually means the
`dialout` membership from step 1 is missing or was added without a fresh login.
The app reports it as:

```
Access to the port '/dev/ttyACM0' is denied.
```

That message is misleading — .NET's `System.IO.Ports` collapses *every* failed
`open()` into that one string, so it also appears for a wrong device path or a
port another process is holding. It is not necessarily a permissions problem.
Get the real reason without the app in the way:

```
ls -l /dev/ttyACM0
id -nG
python3 -c "import os
try:
    fd=os.open('/dev/ttyACM0', os.O_RDWR|os.O_NOCTTY); print('OPEN OK'); os.close(fd)
except OSError as e: print('errno', e.errno, '=', os.strerror(e.errno))"
sudo fuser -v /dev/ttyACM0
```

- **errno 13, permission denied** — account not in `dialout`. Fix with
  `sudo usermod -aG dialout $USER`, then log out and back in. The node itself
  needs no `chmod`; it picks up the new membership on the next open.
- **errno 16, device or resource busy** — something else holds the port.
  `fuser` names it; common culprits are ModemManager, brltty, or a second
  instance of the app.
- **errno 19, no such device** — the device dropped off the bus. Replug it.

To try a group change without logging out, `newgrp dialout` opens a subshell
with it already active; launch the app from there.

## Linux release build (WSL2)

`tools/build-linux-release.ps1` runs `ReleaseManager` under WSL2, mirrors the
source tree into WSL-native storage for a fast restore and publish, and drops the
result in `output\linux` next to `output\win64`.

A Linux host is still required, though not because `ReleaseManager` reads the
host OS — `--platform` overrides that. It is required because `SenderNG.csproj`
selects the Plot 3D plugin's native binaries by build OS, and because keeping
both Linux targets on one host means one toolchain, one mirror, and one runbook.

```powershell
# Linux bundle in output\linux, tests skipped (default)
.\tools\build-linux-release.ps1

# include plugins
.\tools\build-linux-release.ps1 -Plugins

# exercise everything except the ReleaseManager invocation
.\tools\build-linux-release.ps1 -DryRun
```

| Parameter | Default | Effect |
|---|---|---|
| `-Distro` | `Ubuntu` | WSL distribution to build in |
| `-OutRoot` | `<repo>\output` | Output lands in `<OutRoot>\<Platform>` |
| `-Platform` | `linux` | `linux` or `linux-arm64`; passes `--platform` and selects the output folder |
| `-MirrorDir` | `~/.cache/sendeng-linux-build` in WSL | where the source is mirrored; wiped each run |
| `-Plugins` | off | add `--include-plugins` |
| `-SkipTests` | off | pass `--skip-tests` |
| `-NoZip` | off | add `--no-zip` |
| `-DryRun` | off | resolve the SDK, mirror the tree, and report what *would* happen |

A real run replaces its own `output\<Platform>` folder wholesale. The other
output folders — `output\win64`, and whichever of `output\linux` /
`output\linux-arm64` this run is not building — are never read, written, or
deleted.

`-DryRun` writes nothing to `<OutRoot>` at all. It stages a stub bundle inside
the script-owned mirror to prove the mirroring step worked, and leaves any
existing release in place untouched. It does still **wipe and rebuild the
mirror**, exactly as a real run does — so do not start a `-DryRun` while a real
build is using that mirror.

Tests **run by default**: pass `-SkipTests` and the script passes
`--skip-tests` instead.

The script does not install an SDK or touch NuGet credentials. `global.json`
pins `9.0.100` with `rollForward: latestFeature`; the preflight asks the
resolver (`cd "$SRC" && dotnet --version`) for what satisfies that pin rather
than looking for a literal directory.
A path containing a space is rejected, because `wsl.exe` cannot pass one
through as a single argument.

### First real run

Nothing in the test suite performs an actual publish, so the first real run is
the end-to-end check. Two things worth confirming by eye:

- The bundle is executable: `ls -l output\linux\SenderNG\SenderNG` should start
  with `-rwx`. If the exec bit was lost in the copy back, the bundle will not
  launch.
- On a real build the WSL NuGet cache starts cold, so the first run is
  noticeably slower than later ones.

If you interrupt a run with Ctrl+C, check for a leftover `wsl.exe` before
re-running: PowerShell reports the cancellation but does not necessarily kill
the child process, which may still be writing to `output\linux`.

### Linux arm64 (Raspberry Pi)

The target is 64-bit arm64 Linux (Raspberry Pi 4B); the bundle has been
built, installed, and run on a Pi 4B — see the note below.
**The Plot 3D tab runs on arm64** (OpenGL via Mesa) — every plugin ships on
every platform; the tab itself is hidden by default and can be enabled in App
Settings after installing the `plotter-gl` plugin.

Build it from WSL2 on your Windows machine; no .NET SDK on the Pi is needed:

```powershell
# arm64 bundle in output\linux-arm64
.\tools\build-linux-release.ps1 -Platform linux-arm64
```

Output lands in `output\linux-arm64\` as `SenderNG-<version>-linux-arm64.zip`
plus `SHA256SUMS.txt`. The `win64` and `linux` folders are never touched.

For the full recipe — build, copy to the Pi (thumb drive or SSH), install,
verify — see the [arm64 cookbook](docs/arm64-cookbook.md).

The bundle is cross-published (`dotnet publish -r linux-arm64`) from an x64
host, so it is produced on a different machine than it runs on. It has since
been installed and launched on a Pi 4B (window renders, 2D tabs present, no
Plot 3D tab — hidden by default); nothing in either test suite performs a
publish, so every rebuild's final publish is exercised only by a real run.

On the Pi:

- **Serial access needs the `dialout` group.** See [Linux](#linux) above for
  `usermod -aG dialout $USER` and the `newgrp` alternative. Without it the
  Connect tab lists no ports.
- **The app is a GUI and needs a desktop session.** It will not present anything
  useful over a bare SSH shell. Use a local desktop, or forward X11.
- **Use 64-bit Raspberry Pi OS.** A 32-bit userland cannot run a `linux-arm64`
  bundle.

## Contributing

Contributions are welcome. Found a bug or have an idea? [Open an issue](https://github.com/Shkolik/SenderNG/issues) — bug reports and feature requests are just as welcome as code. Improvements are welcome as pull requests: keep changes focused, follow the existing conventions, and make sure `dotnet build SenderNG.sln` and `dotnet test SenderNG.sln` pass. See [AGENT.md](AGENT.md) for the full contributor workflow.

## Acknowledgements

- Terje Io / Io Engineering for ioSender and grblHAL
- AvaloniaUI community
- Silk.NET contributors for the 3D plotter's OpenGL bindings
