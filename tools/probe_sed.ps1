param([string]$Port = "COM9", [string]$OutDir = "hal-dump")
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$sp = New-Object System.IO.Ports.SerialPort($Port, 115200, [System.IO.Ports.Parity]::None, 8, [System.IO.Ports.StopBits]::One)
$sp.ReadTimeout = 1500
$sp.NewLine = "`n"
$sp.Open(); $sp.DtrEnable = $true; $sp.RtsEnable = $true
Start-Sleep -Milliseconds 300

function Get-Resp([string]$cmd) {
    $lines = New-Object System.Collections.Generic.List[string]
    $sp.DiscardInBuffer()
    $sp.Write($cmd + [char]10)
    for ($i = 0; $i -lt 200; $i++) {
        try { $l = $sp.ReadLine() } catch { break }
        $lines.Add($l)
        if ($l.Trim() -eq "ok" -or $l.Trim().StartsWith("error:")) { break }
    }
    return ($lines -join "`n")
}

$ids = @(0..32) + @(37,39,40,41,42,43,44,45,46,47,56,57,58,59,60,61,62,63,64,65) + @(100..133) + @(341..346) + @(370,376,384,392,393,394,398,481,484,485,486,538,539,650,673,676,680,700,701)
$all = New-Object System.Collections.Generic.List[string]
foreach ($id in $ids) {
    $r = Get-Resp ("`$SED=" + $id)
    if ($r -match "SETTINGDESCR") { $all.Add($r) }
}
$all | Set-Content (Join-Path $OutDir "COM9_SED_all.txt")
Write-Host ("captured " + $all.Count + " descriptions")
$sp.Close()
