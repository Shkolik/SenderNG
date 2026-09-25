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

Write-Host ""
if ($script:Failed -gt 0) {
    Write-Host "FAILED: $($script:Failed) failed, $($script:Passed) passed"
    exit 1
}
Write-Host "PASSED: $($script:Passed) assertions"
exit 0
