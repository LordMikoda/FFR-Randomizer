# Draws the FFR Randomizer icon and mod page images with System.Drawing.
# Writes art/icon.ico (used by gui/build.cmd), art/icon-512.png and
# art/banner-1920x1080.png. Run: powershell -ExecutionPolicy Bypass -File tools/make_art.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$art = Join-Path $root "art"
New-Item -ItemType Directory -Force $art | Out-Null

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
$ground = C "#14171f"
$tile = C "#1c212b"
$border = C "#2c3340"
$text = C "#e8ebf0"
$muted = C "#9aa3b2"
$accent = C "#5aa9e6"
# Facet shades, light to dark.
$facets = @{
    table = C "#b9dcf7"; crownL = C "#8cc6f2"; crownR = C "#5aa9e6"
    pavL = C "#4a93cf"; pavC = C "#3a7db8"; pavR = C "#2a6296"
}

function RoundedRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Gem facets in a unit box, drawn into the square at ($x, $y) of side $s.
function DrawGem($g, $x, $y, $s) {
    $shapes = @(
        @("table", @(0.30, 0.18, 0.70, 0.18, 0.62, 0.40, 0.38, 0.40)),
        @("crownL", @(0.10, 0.40, 0.30, 0.18, 0.38, 0.40)),
        @("crownR", @(0.70, 0.18, 0.90, 0.40, 0.62, 0.40)),
        @("pavL", @(0.10, 0.40, 0.38, 0.40, 0.50, 0.88)),
        @("pavC", @(0.38, 0.40, 0.62, 0.40, 0.50, 0.88)),
        @("pavR", @(0.62, 0.40, 0.90, 0.40, 0.50, 0.88))
    )
    foreach ($shape in $shapes) {
        $c = $shape[1]
        $points = for ($i = 0; $i -lt $c.Count; $i += 2) {
            New-Object System.Drawing.PointF ([float]($x + $c[$i] * $s)), ([float]($y + $c[$i + 1] * $s))
        }
        $brush = New-Object System.Drawing.SolidBrush $facets[$shape[0]]
        $g.FillPolygon($brush, [System.Drawing.PointF[]]$points)
        $brush.Dispose()
    }
}

# Two arrows circling the gem: the "shuffle" part of the mark.
function DrawOrbit($g, $cx, $cy, $radius, $width) {
    $pen = New-Object System.Drawing.Pen $accent, $width
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cap = New-Object System.Drawing.Drawing2D.AdjustableArrowCap 2.6, 2.4
    $pen.CustomEndCap = $cap
    $box = New-Object System.Drawing.RectangleF ([float]($cx - $radius)), ([float]($cy - $radius)), ([float](2 * $radius)), ([float](2 * $radius))
    $g.DrawArc($pen, $box, 200, 110)
    $g.DrawArc($pen, $box, 20, 110)
    $pen.Dispose()
}

function NewCanvas($w, $h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

# The app icon: gem on a dark rounded tile. Small sizes drop the orbit so the
# gem stays readable on the taskbar.
function IconBitmap($size) {
    $bmp, $g = NewCanvas $size $size
    $r = [Math]::Max(2, $size * 0.2)
    $path = RoundedRect 0 0 ($size - 1) ($size - 1) $r
    $brush = New-Object System.Drawing.SolidBrush $ground
    $g.FillPath($brush, $path)
    if ($size -ge 32) {
        $pen = New-Object System.Drawing.Pen $border, ([Math]::Max(1, $size / 64))
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }
    if ($size -ge 48) {
        DrawGem $g ($size * 0.22) ($size * 0.20) ($size * 0.56)
        DrawOrbit $g ($size / 2) ($size * 0.505) ($size * 0.37) ($size * 0.045)
    } else {
        DrawGem $g ($size * 0.08) ($size * 0.06) ($size * 0.84)
    }
    $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# A 32-bit bitmap icon entry: header, bottom-up BGRA pixels, then an empty
# AND mask (the alpha channel does the masking).
function IconDib($bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$size); $w.Write([Int32]($size * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]($size * $size * 4)); $w.Write([Int32]0); $w.Write([Int32]0)
    $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $w.Write([byte]$p.B); $w.Write([byte]$p.G); $w.Write([byte]$p.R); $w.Write([byte]$p.A)
        }
    }
    $maskRow = [Math]::Ceiling($size / 32) * 4
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

# An .ico with bitmap entries up to 128 px and a PNG entry at 256, the layout
# every icon reader handles, .NET's included.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bmp = IconBitmap $size
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else {
        $data = IconDib $bmp
    }
    $bmp.Dispose()
    , $data
}
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write($image) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $art "icon.ico"), $ico.ToArray())

$big = IconBitmap 512
$big.Save((Join-Path $art "icon-512.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()

# Mod page banner: the mark on the left, the text block on the right.
# $orbit is the arrows' circle (centre x, centre y, radius); $tx, $ty place the
# title, and $k scales the text block (1 = the 1920x1080 layout).
function Banner($w, $h, $orbit, $tx, $ty, $k, $file) {
    $bmp, $g = NewCanvas $w $h
    $g.Clear($ground)
    $grid = [Math]::Round(60 * $k)
    $gridPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, $border)), 1
    for ($x = 0; $x -le $w; $x += $grid) { $g.DrawLine($gridPen, $x, 0, $x, $h) }
    for ($y = 0; $y -le $h; $y += $grid) { $g.DrawLine($gridPen, 0, $y, $w, $y) }
    $gridPen.Dispose()

    $cx, $cy, $r = $orbit
    $gem = $r * 500 / 330
    DrawGem $g ($cx - $gem / 2) ($cy - $r * 255 / 330) $gem
    DrawOrbit $g $cx $cy $r ($r * 22 / 330)

    function F($size, $name, $style) {
        New-Object System.Drawing.Font $name, ([float]($size * $k)), $style, ([System.Drawing.GraphicsUnit]::Pixel)
    }
    $regular = [System.Drawing.FontStyle]::Regular
    $title = F 118 "Segoe UI" ([System.Drawing.FontStyle]::Bold)
    $sub = F 46 "Segoe UI" $regular
    $small = F 38 "Segoe UI Semibold" $regular
    $textBrush = New-Object System.Drawing.SolidBrush $text
    $mutedBrush = New-Object System.Drawing.SolidBrush $muted
    $accentBrush = New-Object System.Drawing.SolidBrush $accent
    $dot = "  " + [char]0x00B7 + "  "
    $g.DrawString("FFR Randomizer", $title, $textBrush, $tx, $ty)
    $g.DrawString("for Final Fantasy Resonance", $sub, $mutedBrush, ($tx + 8 * $k), ($ty + 150 * $k))
    $g.FillRectangle($accentBrush, ($tx + 12 * $k), ($ty + 260 * $k), (120 * $k), [Math]::Max(3, 6 * $k))
    $g.DrawString(("Chests", "Drops", "Steals", "Shops", "Visions") -join $dot, $small, $textBrush, ($tx + 8 * $k), ($ty + 300 * $k))
    $g.DrawString(("Seeded runs", "Spoiler log", "No game files changed") -join $dot, $small, $mutedBrush, ($tx + 8 * $k), ($ty + 360 * $k))
    $g.Dispose()
    $bmp.Save((Join-Path $art $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Banner 1920 1080 @(480, 545, 330) 900 330 1 "banner-1920x1080.png"
Banner 1300 372 @(200, 186, 148) 400 18 0.74 "banner-1300x372.png"

Write-Output "Wrote icon.ico, icon-512.png and the banners to $art"
