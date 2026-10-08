# SenderNG

**Lightweight cross-platform (Windows + Linux) grblHAL gcode sender based on [ioSender](https://github.com/terjeio/ioSender) by Io Engineering (Terje Io).**

SenderNG is a diverged, focused fork of ioSender — stripped to foam cutting, CNC milling, probing, and USB/Ethernet connectivity. The sender supports as many axes as the controller reports (axis count comes from the firmware, not the app; producing multi-axis g-code is CAM's scope). Lathe, DragKnife and other heavy modules are deferred. Not affiliated with or endorsed by Io Engineering / upstream ioSender.

> **Status:** Private development. Cross-platform Avalonia UI + .NET 9 — engine and shell functional: demo/offline mode, serial + TCP connect, DRO/Jog/Probing widgets, localization (en/ru). Legacy Windows build remains at [Shkolik/ioSender](https://github.com/Shkolik/ioSender) (branch `foamcut`).

## Scope (v0.1)

- Foam cutting and milling
- Probing (Center/Edge/HeightMap)
- Excellon (`.drl`) / HPGL (`.plt`) → g-code converters
- Stepper-calibration wizard
- USB serial + Ethernet (Telnet/WebSocket), YModem/SD
- Program-view plotter plugins: 2D Skia plotter (isometric/flat projection on
  Skia) + 3D Vulkan orbit plotter (`SenderNG.Modules.Plotter.Nex`, HelixToolkit.Nex)
  — design + lib comparison: `docs/machine-config-research.md`
- Camera: deferred — if ever wanted, as a 3rd-party plugin (libs live in
  the decision tree: `docs/ROADMAP.md`)
- Deferred: Lathe wizards, DragKnife

## Origin & License

- Based on `ioSender` `2.0.48` (`terjeio/ioSender`) — see `CNC Core/`, `CNC Controls` for original BSD 3-Clause code.
- **License:** BSD 3-Clause — `LICENSE` retains `Copyright (c) 2019-2023, Io Engineering (Terje Io)` and adds `Copyright (c) 2024-2026, Andrew Shkolik`. See `LICENSE` for full text.
- Avalonia UI (`AvaloniaUI/Avalonia`) is MIT — compatible with BSD.

## Roadmap

1. ✅ `src/SenderNG.Core` (`net9.0`) — engine extracted (`Grbl.cs`, `GCodeParser.cs`, `GCodeEmulator.cs`, `NGCExpr.cs` + foam patches) — done
2. ✅ `src/SenderNG` (`net9.0`) — Avalonia shell, probing UI, settings — done
3. ✅ Program-view plotter plugins — 2D Skia plotter: isometric/flat quadrant
   views + two-carriage iso loft (design: `docs/machine-config-research.md`)
   — done (2026-09-08). 3D Vulkan
   orbit plotter: `src/SenderNG.Modules.Plotter.Nex` (HelixToolkit.Nex), "Plot 3D"
   center tab behind Skia's (order 23), hidden by default. Camera is
   deferred (possible future 3rd-party plugin — see `docs/ROADMAP.md`).
   Linux serial (`/dev/ttyUSB*` / `/dev/ttyACM*`) is already in the sender.

## Development

```bash
# .NET 9 SDK + Avalonia 12 required
dotnet build
dotnet run --project src/SenderNG
```

Running from source on Linux? The `dialout` group requirement applies here as
well — see [Serial port permission errors](#serial-port-permission-errors).

## Running from release assets

Release zips are produced per platform by `tools/ReleaseManager`. On Windows,
build one with:

```bash
dotnet run --project tools/ReleaseManager -- --include-plugins
```

`tools/ReleaseManager/README.md` has the per-platform runbooks and the manual
fallback. (To build the **Linux** zip from a Windows host, use
`tools/build-linux-release.ps1` — see "Linux release build (WSL2)" below; it
writes `output\linux` and never reads, writes or deletes `output\win64`.)
A release folder (`output/win64/` on Windows, `output/linux/` or
`output/linux-arm64/` on Linux) looks like this. The example is an x64
release; an arm64 folder omits the `SenderNG-plugin-plotter-nex-…` zip:

```
SenderNG-<ver>-<suffix>.zip                  ← core bundle: the whole app
SenderNG-plugin-excellon-<ver>-<suffix>.zip
SenderNG-plugin-hpgl-<ver>-<suffix>.zip
SenderNG-plugin-plotter-<ver>-<suffix>.zip
SenderNG-plugin-plotter-nex-<ver>-<suffix>.zip
SenderNG-plugin-probing-<ver>-<suffix>.zip
SHA256SUMS.txt                              ← <sha256> <relative path> per file
```

`<suffix>` is `win64` on Windows, `linux-x64` or `linux-arm64` on Linux.

**Core bundle zip** — the complete self-contained app: `SenderNG.exe` /
`SenderNG` runner, all .NET + platform natives, locale catalogs, and a
`Modules/` folder holding only the SDK assembly
`SenderNG.Modules.Common.dll`. Extract it anywhere and run; no .NET install
needed.

**Plugin zips** — one per optional module (`excellon`, `hpgl`,
`plotter`, `plotter-nex`, `probing`). Each contains the module's own assembly
(`SenderNG.Modules.<X>.dll`) plus an `<id>/` support folder (its locale
catalog; `plotter-nex` also carries its HelixToolkit.Nex/Vulkan closure). The
app discovers modules by scanning `Modules/*.dll` at startup, so installing a
module is a straight copy of the zip's contents into the extracted bundle's
`Modules/` folder: the DLL lands flat next to the SDK, `<id>/` becomes
`Modules/<id>/`. Skip the ones you don't want — the core bundle runs fine
without them. `probing` is the in-tree first-party module
(`src/SenderNG.Modules.Probing`) holding the Probing flyout — tool-length,
edge, center, rotation and height-map probing cycles.

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
   chmod +x SenderNG/SenderNG
   ```
4. Optional modules: same copy step as Windows — plugin contents into
   `SenderNG/Modules/`.
5. Run `./SenderNG/SenderNG` → Connect → open the machine's port
   (`/dev/ttyUSB*` / `/dev/ttyACM*` for serial, or TCP).

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
**The Plot 3D tab is not available on arm64** — see the
[module's platform table](src/SenderNG.Modules.Plotter.Nex/docs/README.md) for
why. Every other module, including the 2D plotter, ships normally.

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
Plot 3D tab); nothing in either test suite performs a publish, so every
rebuild's final publish is exercised only by a real run.

On the Pi:

- **Serial access needs the `dialout` group.** See [Linux](#linux) above for
  `usermod -aG dialout $USER` and the `newgrp` alternative. Without it the
  Connect tab lists no ports.
- **The app is a GUI and needs a desktop session.** It will not present anything
  useful over a bare SSH shell. Use a local desktop, or forward X11.
- **Use 64-bit Raspberry Pi OS.** A 32-bit userland cannot run a `linux-arm64`
  bundle.

## Acknowledgements

- Terje Io / Io Engineering for ioSender and grblHAL
- AvaloniaUI community
- HelixToolkit / HelixToolkit.Nex contributors for the 3D Vulkan plotter
