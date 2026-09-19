# ReleaseManager

Release staging tool for SenderNG. From a clean checkout it publishes the app
(self-contained), stages a curated bundle plus optional plugin payloads into
`./output/`, zips them, and writes `SHA256SUMS.txt`. One command produces a
Windows or Linux release, based on the host OS.

Authoritative design: `docs/superpowers/specs/2026-09-18-releasemanager-design.md`.

## Requirements

- .NET 9 SDK (≥ `9.0.316`, pinned by `global.json`)
- NuGet feeds configured (HelixToolkit GitHub Packages token lives in the
  user-level `NuGet.Config`)
- Run from the **repo root** (`SenderNG.sln` must be in the current directory)

### Linux runtime prerequisites (target machine)

Self-contained publish bundles the .NET runtime but **not** the distro's X11
libraries. Any Debian/Ubuntu machine that runs the Linux bundle — WSL2 for the
smoke test, or the real target — needs Avalonia's X11 dependencies:

```
sudo apt update
sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1
```

Missing these shows up as an unhandled `System.DllNotFoundException` at startup,
e.g. `Unable to load shared library 'libICE.so.6'`. WSL2 needs them too, even
though WSLg supplies the display server.

## Pipeline

1. **Tests** (`dotnet test SenderNG.sln`) unless `--skip-tests` — failure aborts, exit 2.
2. **Publish app** self-contained for the host RID (`win-x64` / `linux-x64`).
3. **Curate** into `output/<platform>/SenderNG` — drops `*.pdb`, `*.xml`,
   dev-only Avalonia diagnostic assemblies, and strips `Modules/` down to only
   `SenderNG.Modules.Common.dll` (the SDK assembly).
4. **Build plugins** (when included) — one `dotnet publish` per plugin.
5. **Verify** the bundle: app exe, `SenderNG.dll`, `.deps.json`,
   `.runtimeconfig.json`, `SenderNG.Core.dll`,
   `Modules/SenderNG.Modules.Common.dll`, and the platform's native runtime
   binaries. No `*.pdb`, no dev-only Avalonia assemblies, no module DLLs in the
   core `Modules/` besides the SDK.
6. **Package** — bundle zip, one zip per plugin, then `SHA256SUMS.txt` covering
   every shipped file. Skip zips with `--no-zip` (checksums still written).
7. **Summary** — platform, app version, plugin ids, file count, artifacts.

## Usage

```
dotnet run --project tools/ReleaseManager [options]
```

| Option | Meaning |
| --- | --- |
| `--include-plugins` | stage all 1st-party and manifest 3rd-party plugins |
| `--plugins <id,id>` | stage only the listed plugin ids (implies `--include-plugins`) |
| `--plugin <csproj>` | add an ad-hoc 3rd-party plugin (repeatable) |
| `--version <ver>` | override the app version read from the csproj |
| `--out <dir>` | output directory (default `./output`) |
| `--skip-tests` | do not run the test suite first |
| `--no-zip` | stage folders + checksums, no zips |
| `--verbose` | per-file detail |
| `-h`, `--help` | show help |

- Default (no flags): core app only, current host OS, zips on.
- No `--platform` flag — the platform is the host (`win64` on Windows, `linux`
  on Linux). Linux bundles build under WSL2 or a real Linux box.
- With **no arguments** in a terminal, ReleaseManager starts an interactive
  checklist (version, plugin toggles, zip choice). On a non-TTY it fails with
  exit 1 ("args required") instead of prompting.

## Output layout

```
output/
├── win64/                                  ← Windows run (linux/ for Linux)
│   ├── SenderNG/                           ← curated app bundle
│   │   ├── SenderNG.exe, *.dll, .deps.json, .runtimeconfig.json
│   │   ├── coreclr.dll, hostfxr.dll, …     ← natives hoisted flat (self-contained)
│   │   ├── Locale/**                       ← app locale catalogs
│   │   └── Modules/SenderNG.Modules.Common.dll  ← SDK assembly, and ONLY it
│   ├── plugins/<plugin-id>/                ← one folder per included plugin
│   │   ├── SenderNG.Modules.<X>.dll        ← flat (the module's own assembly)
│   │   ├── README.txt                      ← install mapping into SenderNG/Modules/
│   │   └── <plugin-id>/                    ← Locale/<id>/ + any closure/native libs
│   ├── SenderNG-<ver>-win64.zip
│   ├── SenderNG-plugin-<id>-<ver>-win64.zip
│   └── SHA256SUMS.txt                      ← <sha256>  <relative path> per file
```

- Linux zip suffix is `linux-x64`; the Linux executable is `SenderNG` (no `.exe`).
- Installing a plugin = copy `plugins/<id>/*` straight into `Modules/`.
- **No `runtimes/` folder**: `dotnet publish` with a single RID (`--self-contained`)
  hoists native binaries flat to the bundle root. The verifier checks for the
  platform core CLR (`coreclr.dll` on win64, `libcoreclr.so` on linux).

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | success (or `--help`) |
| 1 | argument/validation error, unexpected error |
| 2 | test / build / publish failure |
| 3 | staging / verification failure |
| 4 | unsupported host OS |
| 130 | interactive abort (Ctrl-C) |

## Plugin sets

**1st-party** (hardcoded in `FirstPartyPlugins.cs`):

| id | project |
| --- | --- |
| `excellon` | `src/SenderNG.Modules.Excellon` |
| `hpgl` | `src/SenderNG.Modules.Hpgl` |
| `plotter` | `src/SenderNG.Modules.Plotter` |
| `plotter-nex` | `src/SenderNG.Modules.Plotter.Nex` |

`plotter-nex` additionally ships its package closure + native binaries (its
assembly stays flat; `LoadFrom` probes the `plotter-nex/` subfolder for deps).

**3rd-party** (`tools/ReleaseManager/plugins.json`, empty by default):

```json
{
  "plugins": [
    { "id": "myplugin",
      "project": "../path/to/Plugin.csproj",
      "version": "1.2.0" }
  ]
}
```

`version` is optional (defaults to the plugin csproj `<Version>`, else `1.0.0`).
Missing/non-existent project, duplicate ids, or id collisions with the 1st-party
set → exit 1. Staging rules: the module's own assembly DLL goes flat; everything
else it owns goes under `<plugin-id>/` (locale + closure). Host-owned assemblies
(`SenderNG.Core.dll`, `CNC.Core.dll`, `SenderNG.Modules.Common.dll`,
`Avalonia*.dll`) are never re-bundled.

## Runbook — Windows (win64 bundle)

1. From the repo root:
   ```
   dotnet run --project tools/ReleaseManager -- --include-plugins
   ```
   (Bare `dotnet run --project tools/ReleaseManager` shows the interactive
   checklist.)
2. Output: `output/win64/SenderNG`, `output/win64/plugins/<id>/`,
   `SenderNG-<ver>-win64.zip`, per-plugin zips, `SHA256SUMS.txt`.
3. Smoke test: run `output\win64\SenderNG\SenderNG.exe` → Connect → Demo; open
   an `.nc`, plot, probe-free sanity pass.
4. Copy to thumb drive: the self-contained bundle with the Vulkan closure is
   large — copy the **zips** when speed matters and unzip on the destination.
   Optional verification: `Get-FileHash` against `SHA256SUMS.txt`.

## Runbook — Linux under WSL2 → thumb drive

1. **Install WSL2** (Windows 10 2004+ build 19041+, or Windows 11), in
   PowerShell (admin): `wsl --install`. Reboot; finish the Ubuntu setup. If
   already WSL1: `wsl --set-default-version 2`.
2. **Update:** `sudo apt update && sudo apt upgrade -y`
3. **Install .NET SDK 9 + git:**
   ```
   wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
   sudo dpkg -i packages-microsoft-prod.deb
   rm packages-microsoft-prod.deb
   sudo apt update
   sudo apt install -y dotnet-sdk-9.0 git
   ```
   The pinned `9.0.316` rolls forward within `latestPatch`, so any `9.0.3xx`
   SDK is fine. Verify with `dotnet --version`.
4. **Install the runtime X11 packages** (needed for the WSLg smoke test and by
   the bundle on any Ubuntu target — see "Linux runtime prerequisites"):
   ```
   sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1
   ```
5. **Bring in the working tree** (disposable copy; all real work stays on
   Windows):
   ```
   cp -r /mnt/c/Projects/Workshop/SenderNG ~/SenderNG
   cd ~/SenderNG
   find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
   ```
6. **NuGet feeds** (HelixToolkit GitHub Packages needs the token):
   ```
   mkdir -p ~/.nuget/NuGet
   cp "/mnt/c/Users/<you>/AppData/Roaming/NuGet/NuGet.Config" ~/.nuget/NuGet/
   chmod 600 ~/.nuget/NuGet/NuGet.Config
   ```
7. **Restore + sanity build:**
   ```
   dotnet restore
   dotnet build SenderNG.sln
   ```
8. **Release:**
   ```
   dotnet run --project tools/ReleaseManager -- --include-plugins
   ```
   On a headless WSL host the rendering tests (`WboitConeRepro.*`,
   `PlotterLiveRunHeadlessTests.*`) fail without a GPU, which aborts the
   release's test gate. If they fail, ship with `--skip-tests` and rely on the
   WSLg smoke test below instead:
   ```
   dotnet run --project tools/ReleaseManager -- --include-plugins --skip-tests
   ```
   Output: `~/SenderNG/output/linux/`. Optional smoke test via WSLg (renders on
   the Windows desktop): `./output/linux/SenderNG/SenderNG` → Connect → Demo.
   If "Permission denied" (bundle passed through a Windows filesystem):
   `chmod +x ./output/linux/SenderNG/SenderNG` — only the runner needs `+x`, no
   `777`.
   Serial connectivity is **not** testable under WSL2 (Windows COM ports are
   invisible to it; USB passthrough with usbipd-win would tie up the port). Test
   connection on the Windows bundle or a real machine instead.
9. **Copy to the thumb drive** (WSL2 auto-mounts Windows drives under
   `/mnt/<letter>`):
   ```
   ls /mnt/
   cp -r ~/SenderNG/output/linux /mnt/e/SenderNG-linux/
   ```
   Or browse `\\wsl$\<Distro>\home\<user>\SenderNG\output\linux` in Explorer.
   Note: exFAT/NTFS drives don't store Unix permissions — after copying the
   bundle off the drive onto a real Linux machine, run `chmod +x SenderNG`
   before first launch.
10. **Verify on the drive:**
    ```
    cd /mnt/e/SenderNG-linux && sha256sum -c SHA256SUMS.txt
    ```

## Deferred

AppImage/Flatpak packaging (the linux layout is what `linuxdeploy` expects);
Windows→Linux cross-building (the csproj plugin staging selects natives by
build OS, not RID); code/plugin signing; automatic changelogs; GitHub Release
upload automation.
