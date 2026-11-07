$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outPng = Join-Path $PSScriptRoot 'app.png'
$outIco = Join-Path $PSScriptRoot 'app.ico'
$sizes  = 16,20,24,32,40,48,64,96,128,256

function New-RoundedRectPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r){
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-AppBitmap([int]$S){
    $bmp = New-Object System.Drawing.Bitmap($S,$S,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $u = $S / 256.0

    # Charcoal rounded-square background
    $bg = New-RoundedRectPath (10*$u) (10*$u) (236*$u) (236*$u) (54*$u)
    $charcoal = [System.Drawing.Color]::FromArgb(255,47,52,55)
    $bgBrush = New-Object System.Drawing.SolidBrush($charcoal)
    $g.FillPath($bgBrush, $bg)

    # White handwriting stroke with a small hook
    $ink = [System.Drawing.Color]::White
    $pen = New-Object System.Drawing.Pen($ink, (24*$u))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier(
        (72*$u),(176*$u),
        (88*$u),(120*$u),
        (116*$u),(94*$u),
        (152*$u),(92*$u))
    $path.AddBezier(
        (152*$u),(92*$u),
        (174*$u),(90*$u),
        (188*$u),(96*$u),
        (202*$u),(66*$u))
    $g.DrawPath($pen, $path)

    # Small dot at the stroke end
    $dotBrush = New-Object System.Drawing.SolidBrush($ink)
    $g.FillEllipse($dotBrush, (150*$u),(184*$u),(30*$u),(30*$u))

    $pen.Dispose(); $path.Dispose(); $bgBrush.Dispose(); $dotBrush.Dispose(); $bg.Dispose(); $g.Dispose()
    return $bmp
}

# Build PNG entries (PNG-compressed ICO entries are supported on Vista+)
$entries = @()
foreach($s in $sizes){
    $bmp = New-AppBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
    $png = $ms.ToArray()
    if($s -eq 256){ [System.IO.File]::WriteAllBytes($outPng, $png) }
    $entries += ,@($s,$png)
    $bmp.Dispose(); $ms.Dispose()
}

$count = $entries.Count
$ms2 = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms2)
$bw.Write([UInt16]0)              # reserved
$bw.Write([UInt16]1)              # type = icon
$bw.Write([UInt16]$count)         # count

$offset = 6 + 16 * $count
foreach($e in $entries){
    $s = $e[0]; $png = $e[1]
    $b = if($s -ge 256){0}else{$s}
    $bw.Write([Byte]$b)           # width (0 = 256)
    $bw.Write([Byte]$b)           # height
    $bw.Write([Byte]0)            # palette
    $bw.Write([Byte]0)            # reserved
    $bw.Write([UInt16]1)          # planes
    $bw.Write([UInt16]32)         # bpp
    $bw.Write([UInt32]$png.Length)
    $bw.Write([UInt32]$offset)
    $offset += $png.Length
}
foreach($e in $entries){ $bw.Write($e[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outIco, $ms2.ToArray())
$bw.Dispose(); $ms2.Dispose()

Write-Output ("OK  app.png={0} bytes  app.ico={1} bytes" -f (Get-Item $outPng).Length, (Get-Item $outIco).Length)
