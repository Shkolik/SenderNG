$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "release-gate.ps1")
$script:Passed = 0
$script:Failed = 0

function Assert-Equal {
    param($Actual, $Expected, [string]$Because)
    # -ceq, not -eq: PowerShell's -eq is case-insensitive (see
    # build-linux-release.Tests.ps1 for the same rule).
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

function Assert-Match {
    param([string]$Value, [string]$Pattern, [string]$Because)
    if ($Value -match $Pattern) {
        $script:Passed++
        Write-Host "  ok    $Because"
    } else {
        $script:Failed++
        Write-Host "  FAIL  $Because"
        Write-Host "          value:    [$Value]"
        Write-Host "          pattern:  [$Pattern]"
    }
}

Write-Host "Get-CsprojVersion"
$fx = Join-Path $env:TEMP ("release-gate-fx-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $fx | Out-Null
try {
    $csproj = Join-Path $fx "SenderNG.csproj"
    Set-Content -Path $csproj -Value "<Project>`n  <PropertyGroup>`n    <Version>0.1.0</Version>`n  </PropertyGroup>`n</Project>"
    Assert-Equal (Get-CsprojVersion -CsprojPath $csproj) "0.1.0" "reads the Version element"

    Set-Content -Path $csproj -Value "<Project><PropertyGroup><AssemblyVersion>1.2.3.4</AssemblyVersion><Version>2.0.0-rc.1</Version></PropertyGroup></Project>"
    Assert-Equal (Get-CsprojVersion -CsprojPath $csproj) "2.0.0-rc.1" "first Version element wins; AssemblyVersion is not a match"

    Set-Content -Path $csproj -Value "<Project><PropertyGroup></PropertyGroup></Project>"
    Assert-Throws { Get-CsprojVersion -CsprojPath $csproj } "no Version element throws"

    Assert-Throws { Get-CsprojVersion -CsprojPath (Join-Path $fx "missing.csproj") } "missing file throws"

    $repoCsproj = Join-Path (Split-Path -Parent $PSScriptRoot) "src/SenderNG/SenderNG.csproj"
    Assert-Match (Get-CsprojVersion -CsprojPath $repoCsproj) '^\d+\.\d+\.\d+' "reads the real repo csproj"
} finally {
    Remove-Item -LiteralPath $fx -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nGet-ReleaseStrings"
$s = Get-ReleaseStrings -Version "0.1.0" -Stable $true -UtcDate "20261010"
Assert-Equal $s.Version "0.1.0" "stable keeps the csproj version untouched"
Assert-Equal $s.Tag "v0.1.0" "stable tag is v + version"

$s = Get-ReleaseStrings -Version "0.1.0" -Stable $false -UtcDate "20261010"
Assert-Equal $s.Version "0.1.0-20261010" "prerelease appends -YYYYMMDD"
Assert-Equal $s.Tag "v0.1.0-20261010" "prerelease tag is v + suffixed version"

$s = Get-ReleaseStrings -Version "1.2.3" -Stable $false -UtcDate "20270101"
Assert-Equal $s.Tag "v1.2.3-20270101" "suffix uses the injected date, not the clock"

function New-ScratchRepo {
    $dir = Join-Path $env:TEMP ("release-gate-repo-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $dir | Out-Null
    git -C $dir init -q -b main
    Set-Content -Path (Join-Path $dir "file.txt") -Value "one"
    git -C $dir add .
    git -C $dir -c user.email=release-gate@example.invalid -c user.name="release gate test" -c commit.gpgsign=false commit -qm "c1"
    if ($LASTEXITCODE -ne 0) { throw "scratch repo initial commit failed" }
    return $dir
}

function Add-ScratchCommit {
    param([string]$Repo)
    Set-Content -Path (Join-Path $Repo "file.txt") -Value ([guid]::NewGuid().ToString())
    git -C $Repo add .
    git -C $Repo -c user.email=release-gate@example.invalid -c user.name="release gate test" -c commit.gpgsign=false commit -qm "more"
    if ($LASTEXITCODE -ne 0) { throw "scratch repo commit failed" }
}

function Add-ScratchTag {
    param([string]$Repo, [string]$Tag)
    git -C $Repo tag $Tag
    if ($LASTEXITCODE -ne 0) { throw "scratch repo tag failed" }
}

function Remove-ScratchRepo {
    param([string]$Repo)
    Remove-Item -LiteralPath $Repo -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nTest-HasChangesSinceLastTag"
$repo = New-ScratchRepo
try {
    Assert-Equal (Test-HasChangesSinceLastTag -RepoRoot $repo) $true "no tags yet -> build (first run)"

    Add-ScratchTag -Repo $repo -Tag "v0.1.0"
    Assert-Equal (Test-HasChangesSinceLastTag -RepoRoot $repo) $false "tag at HEAD -> quiet week"

    Add-ScratchCommit -Repo $repo
    Assert-Equal (Test-HasChangesSinceLastTag -RepoRoot $repo) $true "commit after the tag -> changed"

    Add-ScratchTag -Repo $repo -Tag "v0.1.0-20990101"
    Assert-Equal (Test-HasChangesSinceLastTag -RepoRoot $repo) $false "newest tag back at HEAD -> quiet week"
} finally {
    Remove-ScratchRepo -Repo $repo
}

$repo2 = New-ScratchRepo
try {
    Add-ScratchTag -Repo $repo2 -Tag "misc-tag"
    Assert-Equal (Test-HasChangesSinceLastTag -RepoRoot $repo2) $true "non-v tags are ignored -> build"
} finally {
    Remove-ScratchRepo -Repo $repo2
}

$notRepo = Join-Path $env:TEMP ("release-gate-notrepo-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $notRepo | Out-Null
try {
    Assert-Throws { Test-HasChangesSinceLastTag -RepoRoot $notRepo } "non-git directory throws"
} finally {
    Remove-Item -LiteralPath $notRepo -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
if ($script:Failed -gt 0) {
    Write-Host "FAILED: $($script:Failed) failed, $($script:Passed) passed"
    exit 1
}
Write-Host "PASSED: $($script:Passed) assertions"
exit 0
