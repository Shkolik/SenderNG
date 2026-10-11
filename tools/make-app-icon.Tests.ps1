$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "make-app-icon.ps1")
$script:Passed = 0
$script:Failed = 0

function Assert-Equal {
    param($Actual, $Expected, [string]$Because)
    # -ceq, not -eq: PowerShell's -eq is case-insensitive (see
    # release-gate.Tests.ps1 for the same rule).
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

function Assert-True {
    param($Value, [string]$Because)
    if ($Value) {
        $script:Passed++
        Write-Host "  ok    $Because"
    } else {
        $script:Failed++
        Write-Host "  FAIL  $Because (was not true)"
    }
}

$png = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\SenderNG\Assets\appIcon.png"))
$committedIco = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\SenderNG\Assets\appIcon.ico"))
$expectedSizes = @(16, 24, 32, 48, 64, 128, 256)

$tmp = Join-Path $env:TEMP ("make-app-icon-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $fresh = Join-Path $tmp "fresh.ico"
    New-AppIcon -Source $png -Output $fresh

    Assert-True (Test-Path -LiteralPath $fresh) "writes the output file"
    $bytes = [System.IO.File]::ReadAllBytes($fresh)

    Assert-Equal ([BitConverter]::ToUInt16($bytes, 0)) 0 "idReserved is 0"
    Assert-Equal ([BitConverter]::ToUInt16($bytes, 2)) 1 "idType is 1 (icon, not cursor)"
    $count = [int][BitConverter]::ToUInt16($bytes, 4)
    Assert-Equal $count 7 "idCount is 7"

    $entries = @()
    for ($i = 0; $i -lt $count; $i++) {
        $e = 6 + (16 * $i)
        $w = [int]$bytes[$e]
        $h = [int]$bytes[($e + 1)]
        $declared = if ($w -eq 0) { 256 } else { $w }
        $entries += [pscustomobject]@{
            Size   = $declared
            Offset = [BitConverter]::ToUInt32($bytes, $e + 12)
            Length = [BitConverter]::ToUInt32($bytes, $e + 8)
        }
        Assert-Equal $h $w "frame $i is square"
        Assert-Equal ([BitConverter]::ToUInt16($bytes, $e + 4)) 1 "frame $i wPlanes is 1"
        Assert-Equal ([BitConverter]::ToUInt16($bytes, $e + 6)) 32 "frame $i wBitCount is 32"
    }

    Assert-Equal (($entries | ForEach-Object { $_.Size }) -join ",") ($expectedSizes -join ",") `
        "frame sizes are exactly 16,24,32,48,64,128,256 in ascending order"

    $cursor = [uint32](6 + (16 * $count))
    foreach ($entry in $entries) {
        Assert-Equal $entry.Offset $cursor "frame at $($entry.Size)px starts exactly where the previous one ended"
        Assert-True (($entry.Offset + $entry.Length) -le $bytes.Length) "frame at $($entry.Size)px stays inside the file"
        $cursor = $entry.Offset + $entry.Length
    }
    Assert-Equal $cursor $bytes.Length "last frame ends exactly at EOF"

    foreach ($entry in $entries) {
        $o = [int]$entry.Offset
        if ($entry.Size -le 64) {
            Assert-Equal ([BitConverter]::ToUInt32($bytes, $o)) 40 "BMP frame $($entry.Size)px has biSize=40"
            Assert-Equal $bytes[($o + 14)] 32 "BMP frame $($entry.Size)px has biBitCount=32"
            Assert-Equal $bytes[($o + 15)] 0 "BMP frame $($entry.Size)px biBitCount high byte is 0"
        } else {
            $magic = ("{0:X2}{1:X2}{2:X2}{3:X2}" -f $bytes[$o], $bytes[($o + 1)], $bytes[($o + 2)], $bytes[($o + 3)])
            Assert-Equal $magic "89504E47" "PNG frame $($entry.Size)px starts with the PNG magic"
        }
    }

    Assert-True (Test-Path -LiteralPath $committedIco) "committed appIcon.ico exists"
    $committed = [System.IO.File]::ReadAllBytes($committedIco)
    Assert-Equal ([Convert]::ToBase64String($bytes)) ([Convert]::ToBase64String($committed)) `
        "regeneration is byte-identical to the committed .ico (drift guard)"
} finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
if ($script:Failed -gt 0) {
    Write-Host "FAILED: $($script:Failed) failed, $($script:Passed) passed"
    exit 1
}
Write-Host "PASSED: $($script:Passed) assertions"
exit 0
