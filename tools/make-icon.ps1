<#
  Renders the AudioSwap icon at every Windows icon size and packs it into a .ico
  (32-bit BMP frames up to 64px, PNG frames above), plus a 256px PNG for the README.
  Small sizes get bolder, simpler artwork so the tray icon stays crisp at 16px.

    powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1 [-PreviewPath sheet.png]
#>
param(
    [string]$IcoPath = (Join-Path $PSScriptRoot '..\src\AudioSwap\Assets\audioswap.ico'),
    [string]$PngPath = (Join-Path $PSScriptRoot '..\docs\icon.png'),
    [string]$PreviewPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Rgb([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

function RoundedRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float][Math]::Min(2 * $r, [Math]::Min($w, $h))
    $p.AddArc([float]$x, [float]$y, $d, $d, 180, 90)
    $p.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# A polygon with softly rounded corners, as one path (so translucent fills don't double up).
function RoundedPolygon([double[][]]$points, [double]$radius, [double]$u, [double]$dy) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $n = $points.Count
    for ($i = 0; $i -lt $n; $i++) {
        $p = $points[$i]; $prev = $points[($i + $n - 1) % $n]; $next = $points[($i + 1) % $n]
        $corner = @()
        foreach ($q in $prev, $next) {
            $vx = $q[0] - $p[0]; $vy = $q[1] - $p[1]; $len = [Math]::Sqrt($vx * $vx + $vy * $vy)
            $k = [Math]::Min($radius, $len / 2) / $len
            $corner += New-Object System.Drawing.PointF([float](($p[0] + $vx * $k) * $u), [float](($p[1] + $vy * $k) * $u + $dy))
        }
        $tip = New-Object System.Drawing.PointF([float]($p[0] * $u), [float]($p[1] * $u + $dy))
        $path.AddBezier($corner[0], $tip, $tip, $corner[1])
    }
    $path.CloseFigure()
    $path
}

# Two clockwise arrows chasing each other around a speaker: "swap audio".
# Geometry is in a 256-unit square; $u scales it to the real size. Smaller sizes drop
# sound waves and thicken everything, so the tray icon still reads at 16px.
function Draw-Mark($g, [double]$u, [int]$S, [System.Drawing.Color]$color, [double]$dy) {
    # Ring: radius, stroke, arc start/sweep, arrowhead width/length.
    # Speaker: box width/height, cone length/mouth height, corner radius, offset left of centre.
    # Waves: radii around the cone mouth, and their stroke.
    if ($S -ge 48)     { $r = 84; $stroke = 18; $start = 206; $sweep = 94; $aw = 48; $ah = 34
                         $bw = 24; $bh = 40; $cw = 30; $ch = 84; $corner = 6; $shift = 46; $waves = @(22, 44); $ws = 12 }
    elseif ($S -ge 24) { $r = 86; $stroke = 22; $start = 204; $sweep = 92; $aw = 58; $ah = 40
                         $bw = 30; $bh = 50; $cw = 32; $ch = 100; $corner = 7; $shift = 44; $waves = @(24); $ws = 16 }
    else               { # Arrowheads at the top and bottom, clear of the speaker's cone.
                         $r = 86; $stroke = 28; $start = 174; $sweep = 92; $aw = 76; $ah = 50
                         $bw = 30; $bh = 46; $cw = 28; $ch = 84; $corner = 8; $shift = 29; $waves = @(); $ws = 0 }
    $waveAngle = if ($S -ge 48) { 44 } else { 38 }
    $c = 128

    $pen = New-Object System.Drawing.Pen($color, [float]($stroke * $u))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $brush = New-Object System.Drawing.SolidBrush($color)
    $box = ($c - $r) * $u
    $d = [float](2 * $r * $u)
    foreach ($from in $start, ($start + 180)) {
        $g.DrawArc($pen, [float]$box, [float]($box + $dy), $d, $d, [float]$from, [float]$sweep)

        # Arrowhead at the arc's end, pointing along the clockwise tangent.
        $a = ($from + $sweep) * [Math]::PI / 180
        $nx = [Math]::Cos($a); $ny = [Math]::Sin($a)   # outward normal
        $tx = -$ny; $ty = $nx                          # clockwise tangent
        $px = $c + $r * $nx - $tx; $py = $c + $r * $ny - $ty
        $head = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([float](($px + $nx * $aw / 2) * $u), [float](($py + $ny * $aw / 2) * $u + $dy))),
            (New-Object System.Drawing.PointF([float](($px - $nx * $aw / 2) * $u), [float](($py - $ny * $aw / 2) * $u + $dy))),
            (New-Object System.Drawing.PointF([float](($px + $tx * $ah) * $u), [float](($py + $ty * $ah) * $u + $dy))))
        $g.FillPolygon($brush, $head)
    }
    $pen.Dispose()

    # Speaker: a box with a flared cone, set left of centre to leave room for its waves.
    $left = $c - $shift; $mouth = $left + $bw + $cw
    $outline = @(
        @($left, ($c - $bh / 2)), @(($left + $bw), ($c - $bh / 2)), @($mouth, ($c - $ch / 2)),
        @($mouth, ($c + $ch / 2)), @(($left + $bw), ($c + $bh / 2)), @($left, ($c + $bh / 2)))
    $speaker = RoundedPolygon $outline $corner $u $dy
    $g.FillPath($brush, $speaker)
    $speaker.Dispose()

    if ($waves.Count -gt 0) {
        $wave = New-Object System.Drawing.Pen($color, [float]($ws * $u))
        $wave.StartCap = $wave.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $wx = $mouth - 6
        foreach ($wr in $waves) {
            $wr += $ws / 2 + 6
            $g.DrawArc($wave, [float](($wx - $wr) * $u), [float](($c - $wr) * $u + $dy), [float](2 * $wr * $u), [float](2 * $wr * $u), [float](-$waveAngle), [float](2 * $waveAngle))
        }
        $wave.Dispose()
    }
    $brush.Dispose()
}

function New-IconBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $u = $S / 256.0

    # Tile: violet -> indigo -> cyan, full-bleed at tray sizes.
    $inset = if ($S -le 24) { 0 } else { 8 * $u }
    $side = $S - 2 * $inset
    $tile = RoundedRect $inset $inset $side $side ($side * 0.235)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(-1, -1)), (New-Object System.Drawing.PointF(($S + 1), ($S + 1))),
        (Rgb 0 0 0), (Rgb 0 0 0))
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend(3)
    $blend.Colors = [System.Drawing.Color[]]@((Rgb 168 85 247), (Rgb 99 102 241), (Rgb 6 182 212))
    $blend.Positions = [single[]]@(0, 0.5, 1)
    $gradient.InterpolationColors = $blend
    $gradient.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    $g.FillPath($gradient, $tile)

    if ($S -ge 32) {
        # Soft light from the top, fading out by the middle.
        $g.SetClip($tile)
        $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF(0, -1)), (New-Object System.Drawing.PointF(0, ($S * 0.6))),
            (Rgb 255 255 255 52), (Rgb 255 255 255 0))
        $sheen.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
        $g.FillRectangle($sheen, 0, 0, $S, [float]($S * 0.6))
        $sheen.Dispose()
        $g.ResetClip()
        # Faint inner edge so the tile reads on dark and light backgrounds.
        $edge = New-Object System.Drawing.Pen((Rgb 255 255 255 40), [float][Math]::Max(1, 2 * $u))
        $g.DrawPath($edge, $tile)
        $edge.Dispose()
        # Drop shadow under the mark.
        Draw-Mark $g $u $S (Rgb 30 16 90 70) (5 * $u)
    }

    Draw-Mark $g $u $S (Rgb 255 255 255) 0
    $gradient.Dispose(); $tile.Dispose(); $g.Dispose()
    $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $S = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $S, $S)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $S)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $stride = $data.Stride
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER; the height counts the colour and mask halves together.
    $w.Write([int]40); $w.Write([int]$S); $w.Write([int]($S * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $S - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $stride, $S * 4) }
    $maskStride = [int]([Math]::Ceiling($S / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskStride * $S)))
    $w.Flush()
    return , $ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    [byte[]]$bytes = if ($size -ge 128) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    [pscustomobject]@{ Size = $size; Bytes = $bytes; Bitmap = $bmp }
}

$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ico)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$f.Bytes.Length); $w.Write([int]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f.Bytes) }
$w.Flush()

function Resolve-OutPath([string]$path) {
    $full = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($path)
    New-Item -ItemType Directory -Force (Split-Path $full) | Out-Null
    $full
}

$IcoPath = Resolve-OutPath $IcoPath
[System.IO.File]::WriteAllBytes($IcoPath, $ico.ToArray())
$PngPath = Resolve-OutPath $PngPath
($frames | Where-Object Size -eq 256).Bitmap.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)

if ($PreviewPath) {
    # Small sizes at 1x with a pixel-exact 4x blow-up below; large sizes at 1x. Dark and light strips.
    $zoom = 4
    $columns = foreach ($f in $frames) { if ($f.Size -le 48) { $f.Size * $zoom } else { $f.Size } }
    $width = [int](($columns | Measure-Object -Sum).Sum + 20 * ($frames.Count + 1))
    $half = 296
    $sheet = New-Object System.Drawing.Bitmap($width, (2 * $half))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.FillRectangle((New-Object System.Drawing.SolidBrush((Rgb 32 32 36))), 0, 0, $width, $half)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush((Rgb 243 243 243))), 0, $half, $width, $half)
    foreach ($band in 0, 1) {
        $x = 20
        $top = $band * $half + 20
        for ($i = 0; $i -lt $frames.Count; $i++) {
            $f = $frames[$i]
            $g.DrawImage($f.Bitmap, $x, $top, $f.Size, $f.Size)
            if ($f.Size -le 48) { $g.DrawImage($f.Bitmap, $x, $top + $f.Size + 8, $f.Size * $zoom, $f.Size * $zoom) }
            $x += $columns[$i] + 20
        }
    }
    $sheet.Save((Resolve-OutPath $PreviewPath), [System.Drawing.Imaging.ImageFormat]::Png)
}

Write-Host "Wrote $IcoPath ($($ico.Length) bytes) and $PngPath"
