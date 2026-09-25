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

function Invoke-LinuxReleaseBuild { throw "not implemented yet" }

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-LinuxReleaseBuild
}
