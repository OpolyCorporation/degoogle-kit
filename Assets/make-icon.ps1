# Converts brand.jpg into brand.png and a multi-size app.ico
Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$jpg = Join-Path $here 'brand.jpg'
$png = Join-Path $here 'brand.png'
$ico = Join-Path $here 'app.ico'

if (-not (Test-Path -LiteralPath $jpg)) { throw "Missing $jpg" }

$src = [System.Drawing.Image]::FromFile($jpg)
try {
    $src.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $blobs = New-Object System.Collections.Generic.List[byte[]]

    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($src, 0, 0, $size, $size)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $blobs.Add($ms.ToArray())
        $ms.Dispose()
        $bmp.Dispose()
    }

    $count = $sizes.Length
    $header = 6
    $dir = 16 * $count
    $offset = $header + $dir
    $fs = [System.IO.File]::Create($ico)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)
    for ($i = 0; $i -lt $count; $i++) {
        $w = $sizes[$i]
        if ($w -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$w) }
        if ($w -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$w) }
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$blobs[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $blobs[$i].Length
    }
    foreach ($blob in $blobs) { $bw.Write($blob) }
    $bw.Flush()
    $bw.Dispose()
    $fs.Dispose()
}
finally {
    $src.Dispose()
}

Write-Output "Wrote $png ($((Get-Item $png).Length) bytes)"
Write-Output "Wrote $ico ($((Get-Item $ico).Length) bytes)"
