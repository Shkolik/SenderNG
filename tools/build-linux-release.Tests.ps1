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

Write-Host ""
if ($script:Failed -gt 0) {
    Write-Host "FAILED: $($script:Failed) failed, $($script:Passed) passed"
    exit 1
}
Write-Host "PASSED: $($script:Passed) assertions"
exit 0
