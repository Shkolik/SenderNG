# ARM64 cookbook: build, ship, install

A repeatable recipe for getting a SenderNG arm64 bundle onto a Raspberry Pi
(Pi 4B or newer, **64-bit** Raspberry Pi OS desktop). Companion to the
[README's arm64 section](../README.md#linux-arm64-raspberry-pi) (build
reference) and the [ReleaseManager runbook](../tools/ReleaseManager/README.md)
(manual release path).

Placeholders: `<user>` is your **desktop login on the Pi** — the account that
owns the graphical session. Install and launch as that user, not as a
throwaway/service account. `<pi-host>` is the Pi's hostname or IP.

## What you get

`output\linux-arm64\` after a build:

- `SenderNG-<version>-linux-arm64.zip` — the app (self-contained .NET, **no SDK needed on the Pi**)
- `SenderNG-plugin-{excellon,hpgl,plotter,probing}-<version>-linux-arm64.zip`
- `SHA256SUMS.txt` — hashes for the zips *and* the staged bundle files

The arm64 build ships the full plugin set — Plotter GL renders through Mesa's
OpenGL stack on the Pi.

## 1. Build (Windows + WSL2)

From the repo root in PowerShell:

```powershell
.\tools\build-linux-release.ps1 -Platform linux-arm64 -Plugins

# quick rebuild without the test pass:
.\tools\build-linux-release.ps1 -Platform linux-arm64 -Plugins -SkipTests
```

- Needs WSL2 (`-Distro` selects the distro, default `Ubuntu`). The .NET SDK
  is resolved inside WSL against the `global.json` pin.
- Output lands in `output\linux-arm64\`; the `win64` and `linux` folders are
  never touched. The first run is slower (cold NuGet cache).
- `-Plugins` is required to produce the plugin zips — without it you only get
  the app. See the README's
  [parameter table](../README.md#linux-release-build-wsl2) for the rest
  (`-OutRoot`, `-DryRun`, …).

Prefer the ReleaseManager CLI directly? Use the
[runbook](../tools/ReleaseManager/README.md) with
`--platform linux-arm64 --include-plugins`.

## 2. Transfer — pick one

### A. Thumb drive

1. On the build machine, copy the five `.zip` files plus `SHA256SUMS.txt`
   from `output\linux-arm64\` onto any USB stick (FAT32/exFAT, ~45 MB total).
2. On the Pi, plug it in — the desktop auto-mounts it under
   `/media/<user>/<LABEL>`. If it doesn't, find and mount it manually:

   ```bash
   lsblk -o NAME,LABEL,SIZE,MOUNTPOINTS
   sudo mount /dev/sda1 /mnt        # adjust the partition
   ```

3. Copy the files home and verify:

   ```bash
   mkdir -p ~/sender-deploy
   cp /media/<user>/<LABEL>/*.zip /media/<user>/<LABEL>/SHA256SUMS.txt ~/sender-deploy/
   cd ~/sender-deploy
   grep '\.zip$' SHA256SUMS.txt | sha256sum -c -   # one "OK" per zip copied
   ```

4. Eject the stick.

### B. SSH

From the build machine — use a WSL shell so globs expand:

```bash
cd /mnt/c/path/to/repo/output/linux-arm64
ssh <user>@<pi-host> mkdir -p sender-deploy
scp SenderNG*.zip SHA256SUMS.txt <user>@<pi-host>:sender-deploy/
```

On the Pi:

```bash
cd ~/sender-deploy
grep '\.zip$' SHA256SUMS.txt | sha256sum -c -   # one "OK" per zip copied
```

`ssh-copy-id <user>@<pi-host>` first if you want passwordless auth.

## 3. Install — as `<user>`, no sudo

Every step below runs as the desktop user: the app lands in that user's home
and the launcher in their Desktop, which is also what the GUI session runs as.

### First install

```bash
# app (the zip contains a SenderNG/ base folder)
unzip -q ~/sender-deploy/SenderNG-<version>-linux-arm64.zip -d ~/

# plugins: copy each zip's contents into Modules/
plug=$(mktemp -d)
for z in ~/sender-deploy/SenderNG-plugin-*-linux-arm64.zip; do
    [ -e "$z" ] || { echo "no plugin zips found - was the build run with -Plugins?"; break; }
    unzip -q "$z" -d "$plug"
    id=$(basename "$z" | sed -E 's/^SenderNG-plugin-([a-z0-9-]+)-[0-9.]+-linux-arm64\.zip$/\1/')
    cp -a "$plug/$id/." ~/SenderNG/Modules/
    echo "installed: $id"
done
rm -rf "$plug"

# the zips are built on Windows and carry no execute bit
chmod +x ~/SenderNG/SenderNG

# desktop launcher
mkdir -p ~/Desktop
cat > ~/Desktop/SenderNG.desktop <<EOF
[Desktop Entry]
Type=Application
Name=SenderNG
Comment=CNC sender (2D only on arm64)
Exec=$HOME/SenderNG/SenderNG
Icon=applications-engineering
Terminal=false
Categories=Development;
EOF
chmod +x ~/Desktop/SenderNG.desktop
```

The plugin glob only matches zips the arm64 build actually produced —
`excellon`, `hpgl`, `plotter`, `plotter-gl`, `probing`.

### Dependencies

None to install on a stock **64-bit Raspberry Pi OS desktop**. Confirm with:

```bash
ldd ~/SenderNG/SenderNG | grep "not found"   # expect no output
```

If it lists anything (stripped-down images), install the Debian package that
provides it — e.g. `sudo apt install libx11-6 libxkbcommon0 libfontconfig1
libgl1` (ICU follows the release: `libicu72` on Bookworm, `libicu76` on
Trixie).

### Update an existing install

Settings and profiles (`global.json`, `profiles/`) live **inside**
`~/SenderNG` (the folder is user-writable), so do not delete it — overwrite
in place:

```bash
pkill -f "$HOME/SenderNG/SenderNG" 2>/dev/null    # stop the running app
unzip -o -q ~/sender-deploy/SenderNG-<version>-linux-arm64.zip -d ~/

plug=$(mktemp -d)                                 # plugins again (cp overwrites)
for z in ~/sender-deploy/SenderNG-plugin-*-linux-arm64.zip; do
    [ -e "$z" ] || { echo "no plugin zips found - was the build run with -Plugins?"; break; }
    unzip -q "$z" -d "$plug"
    id=$(basename "$z" | sed -E 's/^SenderNG-plugin-([a-z0-9-]+)-[0-9.]+-linux-arm64\.zip$/\1/')
    cp -a "$plug/$id/." ~/SenderNG/Modules/
done
rm -rf "$plug"

chmod +x ~/SenderNG/SenderNG                      # unzip -o may reset the bit
```

A **clean reinstall** (`rm -rf ~/SenderNG` first) also wipes those settings —
back up `global.json` and `profiles/` if you want to keep them.

## 4. Launch and verify

- **Double-click the `SenderNG` icon on the desktop.** Never launch over a
  bare SSH shell — there is no display there.
- Expected: window `SenderNG`, menus File/Settings/Help, tabs
  **Program / Console / Plot**, the right-hand Probing/Macros/… panel, and
  status `NOT CONNECTED` until a machine is connected.
- **No Plot 3D tab — by design** on arm64 (see "What you get").
- Serial ports need the `dialout` group:

  ```bash
  groups | grep -qw dialout || sudo usermod -aG dialout $USER   # then log out/in
  ```

  Without it the Connect tab lists no ports.

Remote sanity checks from SSH while the app is open on the Pi's screen:

```bash
pgrep -af 'SenderNG/SenderNG'                # process present
ldd ~/SenderNG/SenderNG | grep -c "not found"   # expect 0
```

## 5. Uninstall

```bash
pkill -f "$HOME/SenderNG/SenderNG" 2>/dev/null
rm -rf ~/SenderNG ~/Desktop/SenderNG.desktop
```

## See also

- [README — Linux arm64 (Raspberry Pi)](../README.md#linux-arm64-raspberry-pi)
- [ReleaseManager runbook](../tools/ReleaseManager/README.md)
- [arm64 support design](superpowers/specs/2026-09-26-arm64-linux-support-design.md)
