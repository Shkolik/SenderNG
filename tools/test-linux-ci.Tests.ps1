$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "test-linux-ci.ps1")
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

Write-Host "ConvertTo-WslPath"
Assert-Equal (ConvertTo-WslPath "C:\Projects\Workshop\SenderNG") "/mnt/c/Projects/Workshop/SenderNG" "drive letter lowercased, backslashes converted"
Assert-Equal (ConvertTo-WslPath "D:\rel\path") "/mnt/d/rel/path" "other drives map to /mnt/<letter>"
Assert-Throws { ConvertTo-WslPath "\\server\share" } "UNC path is rejected"

Write-Host "`nFind-RepoRoot"
$repoRoot = Find-RepoRoot -StartDir $PSScriptRoot
Assert-Equal (Test-Path (Join-Path $repoRoot "SenderNG.sln")) $true "finds the repo root from the tools directory"
Assert-Throws { Find-RepoRoot -StartDir $env:TEMP } "throws when no SenderNG.sln is above the start dir"

Write-Host "`nGet-LinuxCiEnv"
# Not named $env: PowerShell's dynamic scoping means a later Assert-Throws
# block containing $env.TEMP would resolve this OrderedDictionary instead.
$envDefaults = Get-LinuxCiEnv -RepoRootWsl "/mnt/c/r" -MirrorDirWsl "" -DryRun:$false
Assert-Equal $envDefaults.SENDENG_REPO_WSL "/mnt/c/r" "repo path is passed through"
Assert-Equal $envDefaults.SENDENG_MIRROR_WSL "" "empty mirror dir defers to the bash default"
Assert-Equal $envDefaults.SENDENG_FILTER "Category!=Hardware&Category!=GPU&Category!=Timing" "CI's filter is the default"
Assert-Equal $envDefaults.SENDENG_CONFIG "Release" "CI's configuration is the default"
Assert-Equal $envDefaults.SENDENG_CI_DRYRUN "0" "dry run defaults off"

$envCustom = Get-LinuxCiEnv -RepoRootWsl "/mnt/c/r" -MirrorDirWsl "/home/andrew/m" -Filter "FullyQualifiedName~X" `
    -Configuration "Debug" -DryRun
Assert-Equal $envCustom.SENDENG_MIRROR_WSL "/home/andrew/m" "explicit mirror dir is honoured"
Assert-Equal $envCustom.SENDENG_FILTER "FullyQualifiedName~X" "-Filter reaches the payload"
Assert-Equal $envCustom.SENDENG_CONFIG "Debug" "-Configuration reaches the payload"
Assert-Equal $envCustom.SENDENG_CI_DRYRUN "1" "-DryRun sets dry-run"

Write-Host "`nNew-LinuxCiBash"
$bash = New-LinuxCiBash
Assert-Equal (([regex]::Matches($bash, "`r")).Count) 0 "payload contains no CR characters"
Assert-Equal ($bash.EndsWith("# end of script")) $true "payload ends on a comment so the pipeline CRLF is harmless"
Assert-Equal ($bash -match [regex]::Escape('set -euo pipefail')) $true "fails fast"
Assert-Equal ($bash -match [regex]::Escape('export DOTNET_ROOT="$HOME/.dotnet"')) $true "exports DOTNET_ROOT"
Assert-Equal ($bash -match [regex]::Escape('export PATH="$DOTNET_ROOT:$PATH"')) $true "pins ~/.dotnet ahead of the PATH"
Assert-Equal ($bash -match [regex]::Escape('$SENDENG_REPO_WSL')) $true "reads the repo path from the environment, never from substituted text"
Assert-Equal ($bash -match [regex]::Escape('${SENDENG_FILTER:-')) $true "reads the test filter from the environment"
# The two mirrors must never collide: whichever script ran last would otherwise
# wipe the other's tree, and build-linux-release.ps1 also owns output/<platform>.
Assert-Equal ($bash -match [regex]::Escape('$HOME/.cache/senderng-linux-ci')) $true "defaults to its own mirror"
Assert-Equal ($bash -notmatch '(?m)^[^#\n]*sendeng-linux-build') $true "never touches the release script's mirror"
Assert-Equal ($bash -match [regex]::Escape('MARKER="$MIRROR/.senderng-linux-ci-mirror"')) $true "uses its own ownership marker"
Assert-Equal ($bash -match 'rm -rf "\$MIRROR"') $true "wipes the mirror for a fresh tree"
Assert-Equal ($bash -notmatch 'rsync -a --delete') $true "avoids rsync --delete, which fights the exclude list"
Assert-Equal ($bash -match [regex]::Escape("'obj/'")) $true "mirror excludes obj/"
Assert-Equal ($bash -match [regex]::Escape("'bin/'")) $true "mirror excludes bin/"
Assert-Equal ($bash -match [regex]::Escape("'.worktrees/'")) $true "mirror excludes inert worktrees"
Assert-Equal ($bash -match [regex]::Escape("--exclude '.git'")) $true "mirror drops the real .git object store (and a worktree's .git file)"
Assert-Equal ($bash -match [regex]::Escape('mkdir -p "$MIRROR/.git"')) $true "plants the .git marker the localization suites find the checkout by"
Assert-Equal ($bash -match 'cd "\$MIRROR"') $true "runs from the mirror root so SenderNG.sln is the cwd"
# CI parity: these two command lines must stay byte-compatible with ci.yml's steps.
Assert-Equal ($bash -match [regex]::Escape('dotnet build SenderNG.sln -c "$CONFIG" --nologo -t:Rebuild -warnaserror -m:1')) $true "build step matches ci.yml"
Assert-Equal ($bash -match [regex]::Escape('dotnet test SenderNG.sln -c "$CONFIG" --no-build --filter "$FILTER" -m:1')) $true "test step matches ci.yml"
Assert-Equal ($bash -notmatch '(?m)^[^#\n]*ReleaseManager') $true "does not invoke ReleaseManager (that is build-linux-release.ps1's job)"
Assert-Equal ($bash -match '(?m)^\s*exit 2$') $true "build failure exits 2"
Assert-Equal ($bash -match '(?m)^\s*exit 3$') $true "test failure exits 3"
Assert-Equal ($bash -match [regex]::Escape('error|Error\(s\)')) $true "prints build errors rather than a bare exit code"
Assert-Equal ($bash -match [regex]::Escape('Error Message')) $true "prints failing-test details rather than a bare exit count"

Write-Host "`nIntegration: -DryRun against a scratch mirror"
$wslOk = $false
try {
    & wsl.exe -d Ubuntu -e true 2>&1 | Out-Null
    $wslOk = ($LASTEXITCODE -eq 0)
} catch { $wslOk = $false }

if (-not $wslOk) {
    Write-Host "  skip  WSL/Ubuntu unavailable on this machine"
} else {
    $scratchMirror = Join-Path $env:TEMP ("senderng-ci-mirror-" + [guid]::NewGuid().ToString("N"))
    $prev = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "test-linux-ci.ps1") `
            -DryRun -MirrorDir $scratchMirror 2>&1
        $ErrorActionPreference = $prev
        $text = ($out -join "`n")
        Assert-Equal $LASTEXITCODE 0 "dry run exits 0"
        Assert-Equal ($text -match 'DRY RUN') $true "reports the dry run"
        Assert-Equal ($text -match 'would run: dotnet build') $true "prints the build command it would run"
        Assert-Equal ($text -match 'would run: dotnet test') $true "prints the test command it would run"
        Assert-Equal ($text -match [regex]::Escape('Category!=Hardware&Category!=GPU&Category!=Timing')) $true "the filter reached bash through the environment"
        # A dry run must prove the mirror worked without building anything.
        Assert-Equal (Test-Path (Join-Path $scratchMirror "SenderNG.sln")) $true "mirror holds the solution"
        Assert-Equal (Test-Path (Join-Path $scratchMirror ".senderng-linux-ci-mirror")) $true "mirror carries the ownership marker"
        Assert-Equal (Test-Path (Join-Path $scratchMirror ".git")) $true "mirror carries the checkout marker the localization suites need"
        Assert-Equal (Test-Path (Join-Path $scratchMirror "ci-build.log")) $false "dry run built nothing"
        Assert-Equal (Test-Path (Join-Path $scratchMirror "obj")) $false "mirror excludes build output"
    } finally {
        $ErrorActionPreference = $prev
        Remove-Item -LiteralPath $scratchMirror -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
if ($script:Failed -gt 0) {
    Write-Host "FAILED: $($script:Failed) failed, $($script:Passed) passed"
    exit 1
}
Write-Host "PASSED: $($script:Passed) assertions"
