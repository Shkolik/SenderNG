param(
    [switch]$Stable,
    [switch]$SkipChangeCheck,
    [string]$RepoRoot = (Get-Location).Path
)
$ErrorActionPreference = "Stop"

function Get-CsprojVersion {
    param([string]$CsprojPath)
    if (-not (Test-Path -LiteralPath $CsprojPath)) {
        throw "csproj not found: $CsprojPath"
    }
    $text = Get-Content -LiteralPath $CsprojPath -Raw
    $m = [regex]::Match($text, '<Version>([^<]+)</Version>')
    if (-not $m.Success) {
        throw "no <Version> in $CsprojPath; add one"
    }
    return $m.Groups[1].Value
}

function Get-ReleaseStrings {
    param([string]$Version, [bool]$Stable, [string]$UtcDate)
    $ver = if ($Stable) { $Version } else { "$Version-$UtcDate" }
    return [pscustomobject]@{
        Version = $ver
        Tag     = "v$ver"
    }
}

function Test-HasChangesSinceLastTag {
    param([string]$RepoRoot)
    # All three git calls are wrapped in a temporary EAP=Continue: Windows
    # PowerShell 5.1 can turn a redirected native stderr into a terminating
    # error under EAP=Stop (git describe prints "fatal: No names found..." on a
    # repo without tags, git rev-parse on a non-repo). The finally restores the
    # caller's preference even on the throw paths.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        git -C $RepoRoot rev-parse --is-inside-work-tree *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "not a git repository: $RepoRoot"
        }

        $last = git -C $RepoRoot describe --tags --abbrev=0 --match 'v*' 2>&1
        if ($LASTEXITCODE -ne 0) {
            # No v* tag reachable from HEAD: this would be the first release.
            return $true
        }
        $lastTag = ("$last").Trim()

        $commits = @(git -C $RepoRoot log "$lastTag..HEAD" --oneline 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "git log failed for '$lastTag..HEAD' in ${RepoRoot}: $($commits -join '; ')"
        }
        return ($commits.Count -gt 0)
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

function Write-GateOutputs {
    param([string]$Tag, [string]$Version, [bool]$Build, [string]$Date)
    $buildValue = if ($Build) { "true" } else { "false" }
    $lines = @("tag=$Tag", "version=$Version", "build=$buildValue", "date=$Date")
    foreach ($line in $lines) {
        Write-Host $line
    }
    if ($env:GITHUB_OUTPUT) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value $lines
    }
}

function Invoke-ReleaseGate {
    param(
        [switch]$Stable,
        [switch]$SkipChangeCheck,
        [string]$RepoRoot = (Get-Location).Path,
        [string]$UtcDate = ([DateTime]::UtcNow.ToString("yyyyMMdd"))
    )
    $csproj = Join-Path $RepoRoot "src/SenderNG/SenderNG.csproj"
    $baseVersion = Get-CsprojVersion -CsprojPath $csproj
    $strings = Get-ReleaseStrings -Version $baseVersion -Stable ([bool]$Stable) -UtcDate $UtcDate
    $build = if ($SkipChangeCheck) {
        $true
    } else {
        Test-HasChangesSinceLastTag -RepoRoot $RepoRoot
    }
    $result = [pscustomobject]@{
        Tag     = $strings.Tag
        Version = $strings.Version
        Build   = $build
        Date    = $UtcDate
    }
    Write-GateOutputs -Tag $result.Tag -Version $result.Version -Build $result.Build -Date $result.Date
    return $result
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-ReleaseGate -Stable:$Stable -SkipChangeCheck:$SkipChangeCheck -RepoRoot $RepoRoot | Out-Null
}
