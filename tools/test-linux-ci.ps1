param(
    [string]$Distro = "Ubuntu",
    [string]$MirrorDir,
    [string]$Filter = "Category!=Hardware&Category!=GPU&Category!=Timing",
    [string]$Configuration = "Release",
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

function Get-LinuxCiEnv {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRootWsl,
        [string]$MirrorDirWsl = "",
        [string]$Filter = "Category!=Hardware&Category!=GPU&Category!=Timing",
        [string]$Configuration = "Release",
        [switch]$DryRun
    )

    function Flag([bool]$on) { if ($on) { return "1" } else { return "0" } }

    return [ordered]@{
        SENDENG_REPO_WSL   = $RepoRootWsl
        SENDENG_MIRROR_WSL = $MirrorDirWsl
        SENDENG_FILTER     = $Filter
        SENDENG_CONFIG     = $Configuration
        SENDENG_CI_DRYRUN  = (Flag ([bool]$DryRun))
    }
}

function New-LinuxCiBash {
    # A fixed literal, piped over stdin to `bash -s`: every parameter arrives as a
    # SENDENG_* environment variable, so nothing is substituted into this text and
    # there is no cross-shell quoting hazard (wsl.exe eats quotes in argv).
    return @'
#!/usr/bin/env bash
set -euo pipefail

SRC="$SENDENG_REPO_WSL"
# Deliberately NOT ~/.cache/sendeng-linux-build: that mirror belongs to
# build-linux-release.ps1, and running both must not have one wipe the other.
MIRROR="${SENDENG_MIRROR_WSL:-$HOME/.cache/senderng-linux-ci}"
MARKER="$MIRROR/.senderng-linux-ci-mirror"
FILTER="${SENDENG_FILTER:-Category!=Hardware}"
CONFIG="${SENDENG_CONFIG:-Release}"
DRY="${SENDENG_CI_DRYRUN:-0}"

fail() { echo "test-linux-ci: $*" >&2; exit 1; }

# --- 1. dotnet SDK and tools -----------------------------------------------
# Same reasoning as build-linux-release.ps1: the installed SDK lives in
# ~/.dotnet, which is not on PATH by default, so a bare `dotnet` resolves to
# /usr/bin/dotnet and can fail global.json's SDK resolution.
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

command -v dotnet >/dev/null 2>&1 || fail "dotnet is not on PATH in this WSL distribution"
command -v rsync >/dev/null 2>&1 || fail "rsync is not installed in this WSL distribution"
# cd is required: the payload starts in WSL's default cwd, so a bare
# `dotnet --version` would never see global.json.
cd "$SRC" && RESOLVED="$(dotnet --version)" || fail "global.json's pinned SDK cannot be resolved by dotnet"
echo "==> dotnet $RESOLVED; rsync available"

# --- 2. Mirror the source tree into WSL-native storage ---------------------
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

# The real object store is excluded — it is large and nothing here needs history —
# but the localization suites locate the checkout by ".git" plus "src" above the
# test binary, so an empty marker keeps them working. build-linux-release.ps1 needs
# no such thing: ReleaseManager never asks where the checkout is.
mkdir -p "$MIRROR/.git"

[ -f "$MIRROR/SenderNG.sln" ] || fail "mirror is missing SenderNG.sln at $MIRROR"
BUILD_LOG="$MIRROR/ci-build.log"
TEST_LOG="$MIRROR/ci-test.log"
cd "$MIRROR"

if [ "$DRY" = "1" ]; then
    echo "==> DRY RUN: mirroring and SDK resolution only; nothing will be built or tested"
    echo "    would run: dotnet build SenderNG.sln -c $CONFIG --nologo -t:Rebuild -warnaserror -m:1"
    echo "    would run: dotnet test SenderNG.sln -c $CONFIG --no-build --filter \"$FILTER\" -m:1"
    exit 0
fi

# --- 3. Build: exactly ci.yml's Build step ---------------------------------
echo "==> dotnet build SenderNG.sln -c $CONFIG --nologo -t:Rebuild -warnaserror -m:1"
if ! dotnet build SenderNG.sln -c "$CONFIG" --nologo -t:Rebuild -warnaserror -m:1 > "$BUILD_LOG" 2>&1; then
    echo "--- build failed ---"
    grep -E "error|Error\(s\)" "$BUILD_LOG" | tail -20 || true
    echo "full log: $BUILD_LOG"
    exit 2
fi
tail -3 "$BUILD_LOG"

# --- 4. Test: exactly ci.yml's Test step -----------------------------------
echo "==> dotnet test SenderNG.sln -c $CONFIG --no-build --filter \"$FILTER\" -m:1"
set +e
dotnet test SenderNG.sln -c "$CONFIG" --no-build --filter "$FILTER" -m:1 > "$TEST_LOG" 2>&1
RC=$?
set -e

grep -E "Passed!|Failed!" "$TEST_LOG" || true
if [ "$RC" -ne 0 ]; then
    echo ""
    echo "--- failing tests ---"
    grep -E "^  Failed [A-Za-z]" "$TEST_LOG" || true
    echo "--- first failures in detail ---"
    grep -A 12 "Error Message" "$TEST_LOG" | head -60 || true
    echo "full log: $TEST_LOG"
    exit 3
fi

echo ""
echo "OK: Linux CI test pass is green. Log: $TEST_LOG"
# end of script
'@
}

function Invoke-LinuxCiTests {
    param(
        [string]$Distro = "Ubuntu",
        [string]$MirrorDir,
        [string]$Filter = "Category!=Hardware&Category!=GPU&Category!=Timing",
        [string]$Configuration = "Release",
        [switch]$DryRun
    )

    $repoRoot = Find-RepoRoot -StartDir $PSScriptRoot
    $repoWsl = ConvertTo-WslPath $repoRoot
    $mirrorWsl = if ($MirrorDir) { ConvertTo-WslPath $MirrorDir } else { "" }

    # wsl.exe cannot pass a path containing a space through argv.
    foreach ($p in @($repoWsl, $mirrorWsl)) {
        if ($p -match '\s') {
            throw "Path '$p' contains a space, which wsl.exe cannot pass through. Move the repository to a space-free path."
        }
    }

    # Fail with a usable message if the distribution is missing or not running.
    & wsl.exe -d $Distro -e true 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "WSL distribution '$Distro' did not respond to 'wsl.exe -d $Distro -e true'. List installed distributions with 'wsl.exe -l -v'."
    }

    Write-Host "Repo root : $repoRoot"
    Write-Host "Distro    : $Distro"
    Write-Host "Filter    : $Filter"
    Write-Host "Mirror    : $(if ($mirrorWsl) { $mirrorWsl } else { '~/.cache/senderng-linux-ci (default)' })"
    if ($DryRun) { Write-Host "Mode      : DRY RUN (nothing is built or tested)" -ForegroundColor Yellow }
    Write-Host ""

    $envVars = Get-LinuxCiEnv -RepoRootWsl $repoWsl -MirrorDirWsl $mirrorWsl -Filter $Filter `
        -Configuration $Configuration -DryRun:$DryRun

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
        New-LinuxCiBash | & wsl.exe @wslArgs
        $code = $LASTEXITCODE
    } catch [System.Management.Automation.PipelineStoppedException] {
        Write-Host ""
        Write-Warning "Cancelled. If a run is left behind, check for a stray wsl.exe process."
        exit 130
    }

    if ($code -ne 0) {
        Write-Host ""
        Write-Host "FAILED: exit $code" -ForegroundColor Red
        switch ($code) {
            1 { Write-Host "  A precondition failed (SDK, rsync, WSL, or mirror ownership). See above." }
            2 { Write-Host "  Build failed: the same command CI runs. See the log path above." }
            3 { Write-Host "  Tests failed: the same filter CI runs. See the failing tests above." }
            130 { Write-Host "  Cancelled." }
        }
        exit $code
    }

    Write-Host ""
    Write-Host "OK: Linux CI parity pass succeeded" -ForegroundColor Green
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-LinuxCiTests -Distro $Distro -MirrorDir $MirrorDir -Filter $Filter -Configuration $Configuration -DryRun:$DryRun
}
