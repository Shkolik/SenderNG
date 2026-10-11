param(
    [ValidateSet("linux", "linux-arm64")]
    [string]$Platform = "linux",
    [string]$Distro = "Ubuntu",
    [string]$OutRoot,
    [string]$MirrorDir,
    [switch]$Plugins,
    [switch]$NoZip,
    [switch]$SkipTests,
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
        [string]$Platform = "linux",
        [switch]$IncludePlugins,
        [switch]$NoZip,
        [switch]$SkipTests,
        [switch]$DryRun
    )

    function Flag([bool]$on) { if ($on) { return "1" } else { return "0" } }

    return [ordered]@{
        SENDENG_REPO_WSL        = $RepoRootWsl
        SENDENG_OUTROOT_WSL     = $OutRootWsl
        SENDENG_MIRROR_WSL      = $MirrorDirWsl
        SENDENG_INCLUDE_PLUGINS = (Flag ([bool]$IncludePlugins))
        SENDENG_NO_ZIP          = (Flag ([bool]$NoZip))
        SENDENG_SKIP_TESTS      = (Flag ([bool]$SkipTests))
        SENDENG_LINUX_DRYRUN    = (Flag ([bool]$DryRun))
        SENDENG_PLATFORM        = $Platform
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
PLATFORM="${SENDENG_PLATFORM:-linux}"
# For both Linux targets PlatformInfo.FolderName(platform) == platform, so this
# needs no second mapping. The -d check in step 4 asserts ReleaseManager agrees,
# which is what catches the two ever diverging.
LINUX_OUT="$OUTROOT/$PLATFORM"
DRY="${SENDENG_LINUX_DRYRUN:-0}"

fail() { echo "build-linux-release: $*" >&2; exit 1; }

# --- 1. dotnet SDK resolution -------------------------------------------------
# global.json declares the SDK floor and its rollForward policy, so `dotnet`
# must see that file to resolve it. The installed SDK lives at ~/.dotnet, which
# is not on PATH by default, so `dotnet` would otherwise resolve to
# /usr/bin/dotnet and fail SDK resolution.
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

PINNED="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$SRC/global.json")"
# Keep only the first match. Parameter expansion, not `head -n 1`: under
# `set -o pipefail` a producer larger than the pipe buffer is killed by SIGPIPE
# (141) when the reader exits early, and `head` here is far too small to matter
# for a 6-line global.json but would still be a latent false negative.
PINNED="${PINNED%%$'\n'*}"
[ -n "$PINNED" ] || fail "could not read the sdk version from $SRC/global.json"

# Check `dotnet` resolves before asking it anything, so a missing SDK produces
# the tailored message below rather than a raw "command not found".
command -v dotnet >/dev/null 2>&1 || fail "dotnet is not on PATH in this WSL distribution"
AVAILABLE="$(dotnet --list-sdks | awk '{print $1}' | tr '\n' ' ')"
# Ask the resolver instead of matching the pin literally: global.json's
# rollForward policy is part of the answer, so an exact element match rejects
# floors the SDK would happily satisfy. `cd "$SRC"` is required — the payload
# starts in WSL's default cwd, so a bare `dotnet --version` would never see
# global.json. The `if !` context keeps `set -e` from aborting on its
# non-zero status.
if ! RESOLVED="$(cd "$SRC" && dotnet --version 2>&1)"; then
    fail "global.json pins SDK $PINNED, which dotnet cannot resolve.
       installed: $AVAILABLE
       install it with:
         curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
         bash /tmp/dotnet-install.sh --version $PINNED --install-dir \"\$HOME/.dotnet\""
fi

command -v rsync >/dev/null 2>&1 || fail "rsync is not installed in this WSL distribution"
echo "==> dotnet SDK $PINNED resolved as $RESOLVED; rsync available"

# --- 2. Mirror the source tree into WSL-native storage -----------------------
# A Linux host is still required, but not because ReleaseManager reads the host
# OS: --platform overrides that. It is required because both Linux targets then
# share one toolchain, one mirror, and the existing runbooks.
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

ARGS=()
if [ "${SENDENG_SKIP_TESTS:-0}" = "1" ]; then
    ARGS=(--skip-tests)
fi
if [ "${SENDENG_INCLUDE_PLUGINS:-0}" = "1" ]; then
    ARGS+=(--include-plugins)
fi
if [ "${SENDENG_NO_ZIP:-0}" = "1" ]; then
    ARGS+=(--no-zip)
fi
ARGS+=(--platform "$PLATFORM")

echo "==> dotnet run --project tools/ReleaseManager ${ARGS[*]:-}"
if [ "$DRY" = "1" ]; then
    echo "==> DRY RUN: not invoking ReleaseManager; writing a stub bundle"
    mkdir -p "$MIRROR/output/$PLATFORM/SenderNG"
    printf '#!/bin/sh\n' > "$MIRROR/output/$PLATFORM/SenderNG/SenderNG"
    chmod +x "$MIRROR/output/$PLATFORM/SenderNG/SenderNG"
    printf 'dryrun\n' > "$MIRROR/output/$PLATFORM/SHA256SUMS.txt"
else
    set +e
    dotnet run --project tools/ReleaseManager "${ARGS[@]}"
    RC=$?
    set -e
    if [ "$RC" -ne 0 ]; then
        echo "build-linux-release: ReleaseManager failed (exit $RC)" >&2
        exit "$RC"
    fi
fi

# --- 4. Replace this platform's output folder only ---------------------------
# The other platform folders belong to other builds and are never touched here.
# Two independent guards, and neither is redundant:
#   - the basename interlock is the pre-delete safety net. Even though
#     $LINUX_OUT is derived from $PLATFORM, it is the last thing standing
#     between a bad platform value and a wiped output folder.
#   - the -d check is a post-condition. It fails loudly if ReleaseManager wrote
#     a different folder name than this script expects, which would otherwise
#     place the release somewhere nobody looks, with a zero exit code.
[ "$(basename "$LINUX_OUT")" = "$PLATFORM" ] || fail "refusing to replace '$LINUX_OUT': basename does not match the requested platform '$PLATFORM'"
[ -d "$MIRROR/output/$PLATFORM" ] || fail "ReleaseManager did not produce $MIRROR/output/$PLATFORM"
# A dry run must not touch the real output. $MIRROR is script-owned and
# disposable, so the stub written in step 3 is harmless there; $OUTROOT/linux is
# the user's real release, and replacing it with a stub would destroy a good
# build and leave a fake one that later tooling would trust.
if [ "$DRY" = "1" ]; then
    echo "==> DRY RUN: would replace $LINUX_OUT with $MIRROR/output/$PLATFORM"
    echo "    nothing written to $OUTROOT"
else
    echo "==> Replacing $LINUX_OUT"
    if [ -e "$LINUX_OUT" ]; then
        rm -rf "$LINUX_OUT"
    fi
    mkdir -p "$(dirname "$LINUX_OUT")"
    rsync -a "$MIRROR/output/$PLATFORM/" "$LINUX_OUT/"

    [ -d "$LINUX_OUT" ] || fail "copy-back produced nothing at $LINUX_OUT"

    # --- 5. Report ---------------------------------------------------------
    echo "==> $PLATFORM output in $LINUX_OUT"
    find "$LINUX_OUT" -maxdepth 1 -type f \( -name '*.zip' -o -name 'SHA256SUMS.txt' \) \
        -printf '    %f  (%s bytes)\n' | sort
fi
# end of script
'@
    # Normalise to LF, and end on a comment with no trailing newline: PowerShell
    # appends a CRLF record separator to a string piped to a native command, so
    # a trailing LF would leave a bare CR line (`$'\r': command not found`).
    $lf = $raw -replace "`r`n", "`n"
    return $lf.TrimEnd("`n")
}

function Invoke-LinuxReleaseBuild {
    $repoRoot = Find-RepoRoot -StartDir $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($OutRoot)) {
        $OutRoot = Join-Path $repoRoot "output"
    }
    $OutRoot = [System.IO.Path]::GetFullPath($OutRoot)

    $repoWsl = ConvertTo-WslPath $repoRoot
    $outWsl = ConvertTo-WslPath $OutRoot
    $mirrorWsl = ""
    if (-not [string]::IsNullOrWhiteSpace($MirrorDir)) {
        $mirrorWsl = if ($MirrorDir.StartsWith("/")) { $MirrorDir } else { ConvertTo-WslPath $MirrorDir }
    }

    # wsl.exe passes argv through unquoted, so a space would split the value.
    foreach ($p in @($repoWsl, $outWsl, $mirrorWsl)) {
        if ($p -match '\s') {
            throw "Path '$p' contains a space, which wsl.exe cannot pass through. Move the repository to a space-free path."
        }
    }

    # Fail with a usable message if the distribution is missing or not running,
    # rather than letting wsl.exe surface a raw error mid-pipeline.
    & wsl.exe -d $Distro -e true 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "WSL distribution '$Distro' did not respond to 'wsl.exe -d $Distro -e true'. List installed distributions with 'wsl.exe -l -v'."
    }

    Write-Host "Repo root : $repoRoot"
    Write-Host "Platform  : $Platform"
    Write-Host "Output    : $(Join-Path $OutRoot $Platform)"
    Write-Host "Mirror    : $(if ($mirrorWsl) { $mirrorWsl } else { '~/.cache/sendeng-linux-build (default)' })"
    if ($DryRun) { Write-Host "Mode      : DRY RUN (ReleaseManager will not be invoked)" -ForegroundColor Yellow }
    Write-Host ""

    $envVars = Get-LinuxBuildEnv -RepoRootWsl $repoWsl -OutRootWsl $outWsl -MirrorDirWsl $mirrorWsl `
        -Platform $Platform -IncludePlugins:$Plugins -NoZip:$NoZip -SkipTests:$SkipTests -DryRun:$DryRun

    $wslArgs = @("-d", $Distro, "-e", "env")
    foreach ($k in $envVars.Keys) { $wslArgs += "$k=$($envVars[$k])" }
    $wslArgs += @("bash", "-s")

    if ($DryRun) {
        Write-Host "--- wsl invocation ---"
        Write-Host ("wsl.exe " + ($wslArgs -join " "))
        Write-Host "----------------------"
        Write-Host ""
    }

    try {
        New-LinuxBuildBash | & wsl.exe @wslArgs
        $code = $LASTEXITCODE
    } catch [System.Management.Automation.PipelineStoppedException] {
        Write-Host ""
        Write-Warning "Cancelled."
        exit 130
    }

    if ($code -ne 0) {
        Write-Host ""
        Write-Host "FAILED: exit $code" -ForegroundColor Red
        # ExitCodes in Errors.cs is the source of truth; ExitCodeSyncTests verifies these arms against it.
        switch ($code) {
            1 { Write-Host "  A precondition failed. See the message above." }
            2 { Write-Host "  Build failed. See the dotnet output above." }
            3 { Write-Host "  Staging or verification failed. See the ReleaseManager output above." }
            4 { Write-Host "  ReleaseManager does not support the host OS. It must run under Linux." }
            130 { Write-Host "  Cancelled." }
        }
        exit $code
    }

    $linuxOut = Join-Path $OutRoot $Platform
    Write-Host ""
    if ($DryRun) {
        # Never claim a directory exists that this run deliberately did not make.
        Write-Host "OK: dry run complete. Nothing was written to $linuxOut" -ForegroundColor Green
    } else {
        Write-Host "OK: $linuxOut" -ForegroundColor Green
    }
    foreach ($other in @("win64", "linux", "linux-arm64")) {
        if ($other -ne $Platform -and (Test-Path (Join-Path $OutRoot $other))) {
            Write-Host "    (the $other build was not touched)"
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-LinuxReleaseBuild
}
