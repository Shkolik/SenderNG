param(
    [string]$Port = "COM9",
    [string]$OutDir = "hal-dump"
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$sp = $null
try {
    $sp = New-Object System.IO.Ports.SerialPort($Port, 115200, [System.IO.Ports.Parity]::None, 8, [System.IO.Ports.StopBits]::One)
    $sp.ReadTimeout = 3000
    $sp.NewLine = "`n"
    $sp.Open()
    $sp.DtrEnable = $true
    $sp.RtsEnable = $true
    Start-Sleep -Milliseconds 300
    Write-Host "opened $Port"

    function Get-Resp([string]$cmd, [int]$maxLines = 2000) {
        $lines = New-Object System.Collections.Generic.List[string]
        $sp.DiscardInBuffer()
        $sp.Write($cmd + [char]10)
        for ($i = 0; $i -lt $maxLines; $i++) {
            try { $l = $sp.ReadLine() } catch { break }
            $lines.Add($l)
            if ($l.Trim() -eq "ok" -or $l.Trim().StartsWith("error:")) { break }
        }
        return $lines
    }

    $clean = $Port -replace '[^A-Za-z0-9]', ''
    $i = Get-Resp '$I' 300
    ($i -join "`n") | Set-Content (Join-Path $OutDir "$clean`_I.txt")
    Write-Host ("ident: " + (($i -join ' | ').Substring(0, [Math]::Min(100, ($i -join '').Length))))

    if (($i -join ' ') -notmatch "grblHAL") {
        Write-Host "NOT grblHAL on $Port"
        $sp.Close()
        exit 0
    }
    Write-Host "grblHAL confirmed - dumping"
    (Get-Resp '$ES' 3000) | Set-Content (Join-Path $OutDir "$clean`_ES.txt")
    Write-Host "  ES done"
    (Get-Resp '$EG' 3000) | Set-Content (Join-Path $OutDir "$clean`_EG.txt")
    Write-Host "  EG done"
    (Get-Resp '$$' 3000) | Set-Content (Join-Path $OutDir "$clean`_settings.txt")
    Write-Host "  settings done"
    (Get-Resp '$SED=0' 500) | Set-Content (Join-Path $OutDir "$clean`_SED0.txt")
    (Get-Resp '$SED=100' 500) | Set-Content (Join-Path $OutDir "$clean`_SED100.txt")
    (Get-Resp '$SED=1000' 500) | Set-Content (Join-Path $OutDir "$clean`_SED1000.txt")
    Write-Host "done -> $OutDir"
    $sp.Close()
} catch {
    Write-Host "ERROR: $_"
    if ($sp) { try { $sp.Close() } catch {} }
}
