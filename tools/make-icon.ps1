# Generates the Lexi Frames logo.
#   .\make-icon.ps1 -Preview <png>             -> preview sheet of all designs
#   .\make-icon.ps1 -Design 2 -Ico <ico>       -> multi-size .ico of one design
#   .\make-icon.ps1 -Design 3 -Png <png>       -> 256 px PNG of one design
param([string]$Preview, [int]$Design = 1, [string]$Ico, [string]$Png)

Add-Type -AssemblyName System.Drawing
$C = [System.Drawing.Color]
function Col($hex, $a = 255) { $h = $hex.TrimStart('#'); $C::FromArgb($a, [Convert]::ToInt32($h.Substring(0,2),16), [Convert]::ToInt32($h.Substring(2,2),16), [Convert]::ToInt32($h.Substring(4,2),16)) }

$GREEN = '4CE05A'; $CYAN = '2ED3C6'; $YELLOW = 'FFC940'; $RED = 'FF5A40'

function RoundRect($x, $y, $w, $h, $r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = [Math]::Max(1, $r * 2)
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure(); $p
}

function Tile($g, $s) {
  $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Col '2A2D35'), (Col '101114'), 90
  $path = RoundRect 0 0 ($s - 1) ($s - 1) ($s * 0.22)
  $g.FillPath($bg, $path)
  if ($s -ge 32) {
    $hl = New-Object System.Drawing.Pen (Col 'FFFFFF' 28), ([Math]::Max(1, $s / 128))
    $g.DrawPath($hl, $path)
  }
}

function GradPen($s, $hexA, $hexB, $width, $angle = 0) {
  $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Col $hexA), (Col $hexB), $angle
  $pen = New-Object System.Drawing.Pen $br, $width
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
  $pen
}

# ---- Design 1: speedometer gauge with an L ----
function Draw1($g, $s) {
  $cx = $s * 0.5; $cy = $s * 0.54; $r = $s * 0.31
  $w = $s * ([Math]::Max(0.085, 1.6 / $s))
  $box = New-Object System.Drawing.RectangleF ($cx - $r), ($cy - $r), (2 * $r), (2 * $r)
  $track = New-Object System.Drawing.Pen (Col '3A3D46'), $w; $track.StartCap = 'Round'; $track.EndCap = 'Round'
  $g.DrawArc($track, $box, 150, 240)
  $val = GradPen $s $GREEN $YELLOW $w 0
  $g.DrawArc($val, $box, 150, 175)
  # needle
  $a = (150 + 175) * [Math]::PI / 180
  $nl = $r * 0.78
  $needle = New-Object System.Drawing.Pen (Col 'F2F2F2'), ([Math]::Max(1.2, $s * 0.04)); $needle.StartCap = 'Round'; $needle.EndCap = 'Round'
  $g.DrawLine($needle, [float]$cx, [float]$cy, [float]($cx + [Math]::Cos($a) * $nl), [float]($cy + [Math]::Sin($a) * $nl))
  $hub = $s * 0.055
  $g.FillEllipse((New-Object System.Drawing.SolidBrush (Col 'F2F2F2')), [float]($cx - $hub), [float]($cy - $hub), [float](2 * $hub), [float](2 * $hub))
  if ($s -ge 32) {
    $font = New-Object System.Drawing.Font 'Segoe UI', ($s * 0.2), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString('L', $font, (New-Object System.Drawing.SolidBrush (Col $GREEN)), (New-Object System.Drawing.RectangleF 0, ($s * 0.66), $s, ($s * 0.24)), $sf)
  }
}

# ---- Design 2: an L built from frame bars ----
function Draw2($g, $s) {
  $base = $s * 0.78
  $rad = [Math]::Max(1, $s * 0.035)
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, ($s * 0.18), $s, ($s * 0.62)), (Col $CYAN), (Col $GREEN), 90
  # L: tall stem + foot
  $g.FillPath($grad, (RoundRect ($s * 0.22) ($s * 0.18) ($s * 0.17) ($base - $s * 0.18) $rad))
  $g.FillPath($grad, (RoundRect ($s * 0.22) ($base - $s * 0.15) ($s * 0.56) ($s * 0.15) $rad))
  # bars rising from the foot (fps chart)
  $dim = New-Object System.Drawing.SolidBrush (Col 'FFFFFF' 235)
  $bw = $s * 0.1; $gap = $s * 0.055
  $x = $s * 0.22 + $s * 0.17 + $gap
  foreach ($top in 0.46, 0.33) {
    $g.FillPath($dim, (RoundRect $x ($s * $top) $bw ($base - $s * 0.15 - $s * $top - $s * 0.035) $rad))
    $x += $bw + $gap
  }
}

# ---- Design 3: neon L with frame pulse ----
function Draw3($g, $s) {
  $pts = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF ($s * 0.33), ($s * 0.21)),
    (New-Object System.Drawing.PointF ($s * 0.33), ($s * 0.73)),
    (New-Object System.Drawing.PointF ($s * 0.72), ($s * 0.73)))
  if ($s -ge 32) {
    for ($i = 14; $i -ge 1; $i--) {
      $glow = New-Object System.Drawing.Pen (Col $GREEN 9), ($s * (0.13 + 0.013 * $i)); $glow.StartCap = 'Round'; $glow.EndCap = 'Round'; $glow.LineJoin = 'Round'
      $g.DrawLines($glow, $pts)
    }
  }
  $g.DrawLines((GradPen $s $GREEN $CYAN ($s * 0.13) 45), $pts)
  if ($s -ge 32) {
    # small pulse line top-right (frame time)
    $p = New-Object System.Drawing.Pen (Col 'F2F2F2' 230), ([Math]::Max(1, $s * 0.035)); $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p.LineJoin = 'Round'
    $pulse = [System.Drawing.PointF[]]@(
      (New-Object System.Drawing.PointF ($s * 0.50), ($s * 0.40)),
      (New-Object System.Drawing.PointF ($s * 0.58), ($s * 0.40)),
      (New-Object System.Drawing.PointF ($s * 0.63), ($s * 0.25)),
      (New-Object System.Drawing.PointF ($s * 0.69), ($s * 0.52)),
      (New-Object System.Drawing.PointF ($s * 0.73), ($s * 0.40)),
      (New-Object System.Drawing.PointF ($s * 0.80), ($s * 0.40)))
    $g.DrawLines($p, $pulse)
  }
}

function Render($design, $s) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'
  Tile $g $s
  & "Draw$design" $g $s
  $g.Dispose(); $bmp
}

if ($Preview) {
  $W = 3 * 300; $H = 400
  $sheet = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($sheet)
  $g.Clear((Col 'F3F3F3')); $g.TextRenderingHint = 'AntiAliasGridFit'
  $font = New-Object System.Drawing.Font 'Segoe UI', 16, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $small = New-Object System.Drawing.Font 'Segoe UI', 12, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
  $ink = New-Object System.Drawing.SolidBrush (Col '222222'); $muted = New-Object System.Drawing.SolidBrush (Col '666666')
  $names = @('1  Gauge', '2  Frame-bar L', '3  Neon L + pulse')
  for ($d = 1; $d -le 3; $d++) {
    $x0 = ($d - 1) * 300 + 22
    $g.DrawString($names[$d - 1], $font, $ink, $x0, 12)
    $g.DrawImage((Render $d 256), $x0, 42, 256, 256)
    $g.DrawString('desktop / taskbar sizes:', $small, $muted, $x0, 312)
    $g.DrawImage((Render $d 48), $x0, 334, 48, 48)
    $g.DrawImage((Render $d 32), ($x0 + 62), 342, 32, 32)
    $g.DrawImage((Render $d 16), ($x0 + 108), 350, 16, 16)
  }
  $g.Dispose(); $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
}

if ($Png) { (Render $Design 256).Save($Png, [System.Drawing.Imaging.ImageFormat]::Png) }

if ($Ico) {
  $sizes = 16, 24, 32, 48, 64, 128, 256
  $pngs = foreach ($s in $sizes) { $b = Render $Design $s; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); , $ms.ToArray() }
  $out = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $out
  $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
  }
  foreach ($p in $pngs) { $bw.Write($p) }
  [System.IO.File]::WriteAllBytes($Ico, $out.ToArray())
}
