param(
    [string]$Source,
    [string]$Output
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# Frame sizes and the split between BMP and PNG encoding are fixed by the
# design spec (docs/superpowers/specs/2026-10-11-app-icon-design.md). Do not
# drop 24: Avalonia's Win32 IconImpl hardcodes 24px as the Win10+ taskbar size
# and silently upscales from 16 if it is missing.
$script:IconFrameSizes = @(16, 24, 32, 48, 64, 128, 256)
$script:PngFrameSizes = @(128, 256)

function Get-DefaultIconSource {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\SenderNG\Assets\appIcon.png"))
}

function Get-DefaultIconOutput {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\SenderNG\Assets\appIcon.ico"))
}

function Resize-IconBitmap {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory = $true)][int]$Size
    )
    $result = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($result)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $srcRect = New-Object System.Drawing.Rectangle(0, 0, $Bitmap.Width, $Bitmap.Height)
        $dstRect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
        $g.DrawImage($Bitmap, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    } finally {
        $g.Dispose()
    }
    return $result
}

function New-BmpIconFrame {
    param([Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap)
    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $xorStride = $w * 4
    $xor = New-Object byte[] ($xorStride * $h)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        # ICO wants bottom-up rows. Source row (h-1-y) therefore lands at y=0.
        # Format32bppArgb is BGRA on little-endian Windows, which is exactly
        # what the icon XOR mask stores.
        for ($y = 0; $y -lt $h; $y++) {
            $rowPtr = [IntPtr]($data.Scan0.ToInt64() + [int64](($h - 1 - $y) * $data.Stride))
            [System.Runtime.InteropServices.Marshal]::Copy($rowPtr, $xor, $y * $xorStride, $xorStride)
        }
    } finally {
        $Bitmap.UnlockBits($data)
    }

    # The AND mask is Windows 3.1 legacy and is NOT the alpha channel: it is a
    # separate 1bpp bitmap, one bit per pixel, 1 = transparent, rows padded to
    # 32 bits. biHeight is declared as 2*h, so these bytes must exist. GDI-era
    # paths (DrawIconEx, older file managers) still read only this mask and
    # ignore the 32-bit alpha -- zero-filling it would draw our transparent
    # plate corners as black squares at 16px.
    $andStride = ((($w + 31) -shr 5) * 4)
    $and = New-Object byte[] ($andStride * $h)
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $alpha = $xor[($y * $xorStride) + ($x * 4) + 3]
            if ($alpha -eq 0) {
                $index = ($y * $andStride) + ($x -shr 3)
                $and[$index] = $and[$index] -bor (1 -shl (7 - ($x -band 7)))
            }
        }
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $bw.Write([uint32]40)                              # biSize
        $bw.Write([int32]$w)                               # biWidth
        $bw.Write([int32]($h * 2))                         # biHeight = XOR + AND
        $bw.Write([uint16]1)                               # biPlanes
        $bw.Write([uint16]32)                              # biBitCount
        $bw.Write([uint32]0)                               # biCompression = BI_RGB
        $bw.Write([uint32]($xor.Length + $and.Length))    # biSizeImage
        $bw.Write([int32]0)                                # biXPelsPerMeter
        $bw.Write([int32]0)                                # biYPelsPerMeter
        $bw.Write([uint32]0)                               # biClrUsed
        $bw.Write([uint32]0)                               # biClrImportant
        $bw.Write($xor)
        $bw.Write($and)
        $bw.Flush()
        # Unary comma: a bare byte[] return is enumerated by the pipeline and
        # collected as Object[], which breaks BinaryWriter.Write overload
        # resolution at the call site.
        return ,$ms.ToArray()
    } finally {
        $bw.Dispose()
    }
}

function New-PngIconFrame {
    param([Parameter(Mandatory = $true)][System.Drawing.Bitmap]$Bitmap)
    $ms = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        # Unary comma: prevents the pipeline from enumerating the byte[].
        return ,$ms.ToArray()
    } finally {
        $ms.Dispose()
    }
}

function New-AppIcon {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Output
    )
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "source PNG not found: $Source"
    }

    $src = [System.Drawing.Bitmap]::FromFile($Source)
    try {
        $frames = New-Object System.Collections.Generic.List[object]
        foreach ($size in $script:IconFrameSizes) {
            $resized = Resize-IconBitmap -Bitmap $src -Size $size
            try {
                $data = if ($script:PngFrameSizes -contains $size) {
                    New-PngIconFrame -Bitmap $resized
                } else {
                    New-BmpIconFrame -Bitmap $resized
                }
                $frames.Add([pscustomobject]@{ Size = $size; Data = $data })
            } finally {
                $resized.Dispose()
            }
        }
    } finally {
        $src.Dispose()
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $bw.Write([uint16]0)                      # idReserved
        $bw.Write([uint16]1)                      # idType = icon
        $bw.Write([uint16]$frames.Count)          # idCount

        # Entries begin at offset 6, so they are not 4-byte aligned. That is
        # normal for .ico; CreateIconFromResourceEx accepts it and Avalonia's
        # Win32Icon copies misaligned frames into a pooled buffer.
        $offset = [uint32](6 + (16 * $frames.Count))
        foreach ($frame in $frames) {
            $dim = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
            $bw.Write($dim)                       # bWidth (0 means 256)
            $bw.Write($dim)                       # bHeight
            $bw.Write([byte]0)                    # bColorCount
            $bw.Write([byte]0)                    # bReserved
            $bw.Write([uint16]1)                  # wPlanes
            $bw.Write([uint16]32)                 # wBitCount
            $bw.Write([uint32]$frame.Data.Length) # dwBytesInRes
            $bw.Write($offset)                    # dwImageOffset
            $offset += [uint32]$frame.Data.Length
        }
        foreach ($frame in $frames) {
            $bw.Write($frame.Data)
        }
        $bw.Flush()

        $dir = Split-Path -Parent $Output
        if ($dir -and -not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        [System.IO.File]::WriteAllBytes($Output, $ms.ToArray())
    } finally {
        $bw.Dispose()
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $srcPath = if ($Source) { $Source } else { Get-DefaultIconSource }
    $outPath = if ($Output) { $Output } else { Get-DefaultIconOutput }
    New-AppIcon -Source $srcPath -Output $outPath
    Write-Host "wrote $outPath"
}
