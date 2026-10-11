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
