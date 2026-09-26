$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-linux-release.ps1")
$script:Passed = 0
$script:Failed = 0

function Assert-Equal {
    param($Actual, $Expected, [string]$Because)
    # -ceq, not -eq: PowerShell's -eq is case-insensitive, which would let a
    # regression to /mnt/C/ pass an assertion named "drive letter lowercased".
    if ([string]$Actual -ceq [string]$Expected) {
        $script:Passed++
        Write-Host "  ok    $Because"
    } else {
        $script:Failed++
        Write-Host "  FAIL  $Because"
        Write-Host "          expected: [$Expected]"
        Write-Host "          actual:   [$Actual]"
    }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Because)
    try {
        & $Action | Out-Null
        $script:Failed++
        Write-Host "  FAIL  $Because (nothing was thrown)"
    } catch {
        $script:Passed++
        Write-Host "  ok    $Because"
    }
}

function Assert-NotNull {
    param($Value, [string]$Because)
    if ($null -ne $Value) {
        $script:Passed++
        Write-Host "  ok    $Because"
    } else {
        $script:Failed++
        Write-Host "  FAIL  $Because (was null)"
    }
}

Write-Host "ConvertTo-WslPath"
Assert-Equal (ConvertTo-WslPath "C:\Projects\Workshop\SenderNG") "/mnt/c/Projects/Workshop/SenderNG" "drive letter lowercased, backslashes converted"
Assert-Equal (ConvertTo-WslPath "C:/Projects/Workshop/SenderNG") "/mnt/c/Projects/Workshop/SenderNG" "forward slashes accepted"
Assert-Equal (ConvertTo-WslPath "D:\rel\path") "/mnt/d/rel/path" "other drives map to /mnt/<letter>"
Assert-Equal (ConvertTo-WslPath "C:\a b\c") "/mnt/c/a b/c" "spaces preserved verbatim"
Assert-Equal (ConvertTo-WslPath "C:\a\..\b") "/mnt/c/b" "dot-dot segments are normalised"
Assert-Throws { ConvertTo-WslPath "\\server\share" } "UNC path is rejected"

Write-Host "`nFind-RepoRoot"
$repoRoot = Find-RepoRoot -StartDir $PSScriptRoot
Assert-Equal (Test-Path (Join-Path $repoRoot "SenderNG.sln")) $true "finds the repo root from the tools directory"
Assert-Throws { Find-RepoRoot -StartDir $env:TEMP } "throws when no SenderNG.sln is above the start dir"

Write-Host "`nGet-LinuxBuildEnv"
# Not named $env: PowerShell's dynamic scoping means a later Assert-Throws
# block containing $env.TEMP would resolve this OrderedDictionary instead of
# the environment provider, and pass for the wrong reason.
$envDefault = Get-LinuxBuildEnv -RepoRootWsl "/mnt/c/r" -OutRootWsl "/mnt/c/r/output" -MirrorDirWsl "" `
        -IncludePlugins:$false -NoZip:$false -RunTests:$false -DryRun:$false
Assert-Equal $envDefault.SENDENG_REPO_WSL "/mnt/c/r" "repo path is passed through"
Assert-Equal $envDefault.SENDENG_OUTROOT_WSL "/mnt/c/r/output" "output root is passed through"
Assert-NotNull $envDefault.SENDENG_MIRROR_WSL "mirror key exists (guards the empty-string check below)"
Assert-Equal $envDefault.SENDENG_MIRROR_WSL "" "empty mirror dir defers to the bash default"
Assert-Equal $envDefault.SENDENG_INCLUDE_PLUGINS "0" "plugins default off"
Assert-Equal $envDefault.SENDENG_NO_ZIP "0" "zip default on"
Assert-Equal $envDefault.SENDENG_RUN_TESTS "0" "tests default to skipped"
Assert-Equal $envDefault.SENDENG_LINUX_DRYRUN "0" "dry run defaults off"

$envAll = Get-LinuxBuildEnv -RepoRootWsl "/mnt/c/r" -OutRootWsl "/mnt/c/r/output" -MirrorDirWsl "/home/andrew/m" `
            -IncludePlugins -NoZip -RunTests -DryRun
Assert-Equal $envAll.SENDENG_MIRROR_WSL "/home/andrew/m" "explicit mirror dir is honoured"
Assert-Equal $envAll.SENDENG_INCLUDE_PLUGINS "1" "-Plugins sets include-plugins"
Assert-Equal $envAll.SENDENG_NO_ZIP "1" "-NoZip sets no-zip"
Assert-Equal $envAll.SENDENG_RUN_TESTS "1" "-RunTests sets run-tests"
Assert-Equal $envAll.SENDENG_LINUX_DRYRUN "1" "-DryRun sets dry-run"

Write-Host "`nNew-LinuxBuildBash"
$bash = New-LinuxBuildBash
Assert-Equal (([regex]::Matches($bash, "`r")).Count) 0 "payload contains no CR characters"
Assert-Equal ($bash.EndsWith("# end of script")) $true "payload ends on a comment so the pipeline CRLF is harmless"
Assert-Equal ($bash -match [regex]::Escape('export PATH="$DOTNET_ROOT:$PATH"')) $true "pins ~/.dotnet ahead of the PATH"
Assert-Equal ($bash -match [regex]::Escape('export DOTNET_ROOT="$HOME/.dotnet"')) $true "exports DOTNET_ROOT"
Assert-Equal ($bash -match [regex]::Escape('$HOME/.cache/sendeng-linux-build')) $true "default mirror is script-owned, not a real clone"
Assert-Equal ($bash -match [regex]::Escape('MARKER="$MIRROR/.sendeng-linux-build-mirror"')) $true "uses an ownership marker"
Assert-Equal ($bash -match 'rm -rf "\$MIRROR"') $true "wipes the mirror for a fresh tree"
Assert-Equal ($bash -notmatch 'rsync -a --delete') $true "avoids rsync --delete, which fights the exclude list"
Assert-Equal ($bash -match [regex]::Escape("'obj/'")) $true "mirror excludes obj/"
Assert-Equal ($bash -match [regex]::Escape("'bin/'")) $true "mirror excludes bin/"
Assert-Equal ($bash -match [regex]::Escape("'output/'")) $true "mirror excludes output/"
Assert-Equal ($bash -match '(?m)^\s+dotnet run --project tools/ReleaseManager') $true "invokes ReleaseManager"
Assert-Equal ($bash -match 'cd "\$MIRROR"') $true "runs from the mirror root so SenderNG.sln is the cwd"
Assert-Equal ($bash -match [regex]::Escape('LINUX_OUT="$OUTROOT/linux"')) $true "target is output/linux"
Assert-Equal ($bash -match 'basename "\$LINUX_OUT"') $true "guards the delete against a non-linux folder name"
Assert-Equal (([regex]::Matches($bash, '(?m)^[^#\n]*\bwin64\b')).Count) 0 "win64 never appears on an executable line"
Assert-Equal ($bash -notmatch 'rm -rf[^#\n]*win64') $true "no rm ever targets win64"
Assert-Equal ($bash -match [regex]::Escape('exit "$RC"')) $true "propagates the ReleaseManager exit code"
Assert-Equal ($bash -match [regex]::Escape('DRY RUN: would replace')) $true "dry run reports the copy-back instead of performing it"
Assert-Equal ($bash -match 'rm -rf "\$LINUX_OUT"') $true "a real run still wipes output/linux so the copy-back is fresh"

Write-Host "`nDry run against a scratch output root"
$scratch = Join-Path $env:TEMP ("sendeng-linux-test-" + [guid]::NewGuid().ToString("N"))
$scratchMirror = Join-Path $env:TEMP ("sendeng-linux-mirror-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$prev = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    # An owned -MirrorDir keeps the test off the real ~/.cache mirror and lets it
    # assert on the dry-run stub, which lives in the mirror and not the output.
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-linux-release.ps1") `
        -DryRun -OutRoot $scratch -MirrorDir $scratchMirror 2>&1
    $ErrorActionPreference = $prev
    Assert-Equal $LASTEXITCODE 0 "dry run exits 0"
    Assert-Equal (Test-Path (Join-Path $scratch "linux")) $false "dry run wrote no linux folder to the output root"
    Assert-Equal (Test-Path (Join-Path $scratch "win64")) $false "dry run created no win64 folder"
    Assert-Equal (Test-Path (Join-Path $scratchMirror "output\linux\SHA256SUMS.txt")) $true "dry run staged its stub inside the script-owned mirror"
    # Read the pin from global.json rather than hardcoding it, so a legitimate
    # SDK bump does not fail a test whose message would point at the test.
    $sdkPin = (Get-Content (Join-Path $repoRoot "global.json") -Raw | ConvertFrom-Json).sdk.version
    Assert-Equal (($out -join "`n") -match [regex]::Escape($sdkPin)) $true "dry run reports the pinned SDK"
} finally {
    $ErrorActionPreference = $prev
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scratchMirror -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nExisting win64 and linux builds survive a dry run"
$scratch2 = Join-Path $env:TEMP ("sendeng-win64-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path (Join-Path $scratch2 "win64") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $scratch2 "linux") | Out-Null
$exe = Join-Path $scratch2 "win64\SenderNG.exe"
Set-Content -LiteralPath $exe -Value "MZ" -NoNewline
# A pre-existing linux build is exactly what a dry run must not destroy. The
# content is meaningless; any write, delete or replace changes the hash.
$sums = Join-Path $scratch2 "linux\SHA256SUMS.txt"
Set-Content -LiteralPath $sums -Value "pre-existing-build" -NoNewline
$before = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$beforeSums = (Get-FileHash -LiteralPath $sums -Algorithm SHA256).Hash
# An owned mirror keeps the suite hermetic. Without it this group would wipe
# the real ~/.cache/sendeng-linux-build on every run, which makes concurrent
# suite runs race and yanks the mirror out from under a real build.
$scratchMirror2 = Join-Path $env:TEMP ("sendeng-linux-mirror2-" + [guid]::NewGuid().ToString("N"))
$prev = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $out2 = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-linux-release.ps1") `
        -DryRun -OutRoot $scratch2 -MirrorDir $scratchMirror2 2>&1
    $ErrorActionPreference = $prev
    Assert-Equal $LASTEXITCODE 0 "dry run beside existing builds exits 0"
    Assert-Equal (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash $before "win64 file is byte-identical after the run"
    Assert-Equal ((Get-ChildItem (Join-Path $scratch2 "win64")).Count) 1 "no files were added to win64"
    Assert-Equal (Get-FileHash -LiteralPath $sums -Algorithm SHA256).Hash $beforeSums "a pre-existing linux build is not clobbered by a dry run"
} finally {
    $ErrorActionPreference = $prev
    Remove-Item -LiteralPath $scratch2 -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scratchMirror2 -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nMirror ownership guard"
# Preconditions: WSL present, a distro named Ubuntu, and a real clone at
# $HOME/SenderNG. `echo $HOME/SenderNG` always yields a non-empty string, so
# this block never silently skips - on a machine without those preconditions it
# goes red, which is the right direction for a regression guard.
$realClone = (& wsl.exe -d Ubuntu -e bash -lc 'echo $HOME/SenderNG').Trim()
if ($realClone) {
    $prev = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $out3 = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-linux-release.ps1") -DryRun -MirrorDir $realClone 2>&1
        $ErrorActionPreference = $prev
        Assert-Equal ($LASTEXITCODE -ne 0) $true "refuses to wipe a directory it did not create"
        Assert-Equal (($out3 -join "`n") -match 'not created by this script') $true "explains why it refused"
        # The mirror path is a WSL path, so it must be checked from inside WSL.
        $survived = & wsl.exe -d Ubuntu -e bash -lc "test -f '$realClone/SenderNG.sln' && test -d '$realClone/.git' && echo yes"
        Assert-Equal ($survived -join "").Trim() "yes" "the pre-existing clone is fully intact"
    } finally {
        $ErrorActionPreference = $prev
    }
}

Write-Host ""
if ($script:Failed -gt 0) {
    Write-Host "FAILED: $($script:Failed) failed, $($script:Passed) passed"
    exit 1
}
Write-Host "PASSED: $($script:Passed) assertions"
exit 0
