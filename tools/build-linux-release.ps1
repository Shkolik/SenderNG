param(
    [string]$Distro = "Ubuntu",
    [string]$OutRoot,
    [string]$MirrorDir,
    [switch]$Plugins,
    [switch]$NoZip,
    [switch]$RunTests,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function ConvertTo-WslPath {
    param([Parameter(Mandatory = $true)][string]$WindowsPath)

    $full = [System.IO.Path]::GetFullPath($WindowsPath)
    if ($full -notmatch '^([A-Za-z]):[\\/](.*)$') {
        throw "Not a drive-rooted Windows path: '$WindowsPath'"
    }
    $drive = $Matches[1].ToLowerInvariant()
    $rest = $Matches[2] -replace '\\', '/'
    return "/mnt/$drive/$rest"
}

function Find-RepoRoot {
    param([Parameter(Mandatory = $true)][string]$StartDir)

    $dir = [System.IO.Path]::GetFullPath($StartDir)
    while ($true) {
        if (Test-Path -LiteralPath (Join-Path $dir "SenderNG.sln")) {
            return $dir
        }
        $parent = Split-Path -Parent $dir
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $dir) {
            throw "SenderNG.sln not found at or above '$StartDir'. Run this script from inside the repository."
        }
        $dir = $parent
    }
}

function Get-LinuxBuildEnv {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRootWsl,
        [Parameter(Mandatory = $true)][string]$OutRootWsl,
        [string]$MirrorDirWsl = "",
        [switch]$IncludePlugins,
        [switch]$NoZip,
        [switch]$RunTests,
        [switch]$DryRun
    )

    function Flag([bool]$on) { if ($on) { return "1" } else { return "0" } }

    return [ordered]@{
        SENDENG_REPO_WSL        = $RepoRootWsl
        SENDENG_OUTROOT_WSL     = $OutRootWsl
        SENDENG_MIRROR_WSL      = $MirrorDirWsl
        SENDENG_INCLUDE_PLUGINS = (Flag ([bool]$IncludePlugins))
        SENDENG_NO_ZIP          = (Flag ([bool]$NoZip))
        SENDENG_RUN_TESTS       = (Flag ([bool]$RunTests))
        SENDENG_LINUX_DRYRUN    = (Flag ([bool]$DryRun))
    }
}

function New-LinuxBuildBash {
    $raw = @'
#!/usr/bin/env bash
set -euo pipefail

# Parameters arrive as SENDENG_* environment variables so this payload stays a
# fixed literal with no placeholder substitution.

SRC="$SENDENG_REPO_WSL"
OUTROOT="$SENDENG_OUTROOT_WSL"
MIRROR="${SENDENG_MIRROR_WSL:-$HOME/.cache/sendeng-linux-build}"
MARKER="$MIRROR/.sendeng-linux-build-mirror"
LINUX_OUT="$OUTROOT/linux"
DRY="${SENDENG_LINUX_DRYRUN:-0}"

fail() { echo "build-linux-release: $*" >&2; exit 1; }

# --- 1. dotnet SDK resolution -------------------------------------------------
# global.json pins 9.0.316 with rollForward latestPatch. It is already installed
# at ~/.dotnet, but that directory is not on PATH by default, so `dotnet` would
# otherwise resolve to /usr/bin/dotnet (9.0.121) and fail SDK resolution.
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

PINNED="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$SRC/global.json" | head -n 1)"
[ -n "$PINNED" ] || fail "could not read the sdk version from $SRC/global.json"

AVAILABLE="$(dotnet --list-sdks | awk '{print $1}' | tr '\n' ' ')"
if ! dotnet --list-sdks | awk '{print $1}' | grep -Fxq "$PINNED"; then
    fail "global.json pins SDK $PINNED, which dotnet cannot resolve.
       installed: $AVAILABLE
       install it with:
         curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
         bash /tmp/dotnet-install.sh --version $PINNED --install-dir \"\$HOME/.dotnet\""
fi

command -v rsync >/dev/null 2>&1 || fail "rsync is not installed in this WSL distribution"
echo "==> dotnet SDK $PINNED resolved; rsync available"

# --- 2. Mirror the source tree into WSL-native storage -----------------------
# A real Linux build is required: ReleaseManager derives the platform from the
# host OS, and SenderNG.csproj selects Nex natives by build OS, not by RID.
#
# The mirror is wiped and rebuilt rather than synced with `rsync --delete`:
# --delete cannot remove a destination directory whose contents are protected
# by the exclude list, and wiping guarantees a genuinely fresh tree every run.
echo "==> Mirroring $SRC -> $MIRROR"
if [ -e "$MIRROR" ] && [ ! -f "$MARKER" ]; then
    fail "'$MIRROR' exists but was not created by this script (no $MARKER).
       Refusing to delete it. Pass -MirrorDir with a different path, or
       remove '$MIRROR' yourself."
fi
rm -rf "$MIRROR"
mkdir -p "$MIRROR"
: > "$MARKER"
rsync -a \
    --exclude '.git/' --exclude 'bin/' --exclude 'obj/' \
    --exclude 'output/' --exclude '.vs/' --exclude '.debug-repro/' \
    --exclude '.worktrees/' --exclude 'TestResults/' --exclude '.superpowers/' \
    "$SRC/" "$MIRROR/"

[ -f "$MIRROR/SenderNG.sln" ] || fail "mirror is missing SenderNG.sln at $MIRROR"

# --- 3. Build via ReleaseManager ---------------------------------------------
# Program.cs uses Directory.GetCurrentDirectory() as the repo root, so cwd must
# be the mirror.
cd "$MIRROR"

ARGS=(--skip-tests)
if [ "$SENDENG_RUN_TESTS" = "1" ]; then
    ARGS=()
fi
if [ "$SENDENG_INCLUDE_PLUGINS" = "1" ]; then
    ARGS+=(--include-plugins)
fi
if [ "$SENDENG_NO_ZIP" = "1" ]; then
    ARGS+=(--no-zip)
fi

echo "==> dotnet run --project tools/ReleaseManager -- ${ARGS[*]:-}"
if [ "$DRY" = "1" ]; then
    echo "==> DRY RUN: not invoking ReleaseManager; writing a stub bundle"
    mkdir -p "$MIRROR/output/linux/SenderNG"
    printf '#!/bin/sh\n' > "$MIRROR/output/linux/SenderNG/SenderNG"
    chmod +x "$MIRROR/output/linux/SenderNG/SenderNG"
    printf 'dryrun\n' > "$MIRROR/output/linux/SHA256SUMS.txt"
else
    set +e
    dotnet run --project tools/ReleaseManager -- "${ARGS[@]}"
    RC=$?
    set -e
    if [ "$RC" -ne 0 ]; then
        echo "build-linux-release: ReleaseManager failed (exit $RC)" >&2
        exit "$RC"
    fi
fi

# --- 4. Replace output/linux only -------------------------------------------
# The win64 folder belongs to the Windows build and is never touched here.
[ "$(basename "$LINUX_OUT")" = "linux" ] || fail "refusing to replace '$LINUX_OUT': not a 'linux' output folder"
echo "==> Replacing $LINUX_OUT"
if [ -e "$LINUX_OUT" ]; then
    rm -rf "$LINUX_OUT"
fi
mkdir -p "$(dirname "$LINUX_OUT")"
rsync -a "$MIRROR/output/linux/" "$LINUX_OUT/"

[ -d "$LINUX_OUT" ] || fail "copy-back produced nothing at $LINUX_OUT"

# --- 5. Report ---------------------------------------------------------------
echo "==> Linux output in $LINUX_OUT"
find "$LINUX_OUT" -maxdepth 1 -type f \( -name '*.zip' -o -name 'SHA256SUMS.txt' \) \
    -printf '    %f  (%s bytes)\n' | sort
# end of script
'@
    # Normalise to LF, and end on a comment with no trailing newline: PowerShell
    # appends a CRLF record separator to a string piped to a native command, so
    # a trailing LF would leave a bare CR line (`$'\r': command not found`).
    $lf = $raw -replace "`r`n", "`n"
    return $lf.TrimEnd("`n")
}

function Invoke-LinuxReleaseBuild { throw "not implemented yet" }

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-LinuxReleaseBuild
}
