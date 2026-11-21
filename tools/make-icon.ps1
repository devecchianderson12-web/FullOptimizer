# Genera assets\FullOptimizer.ico (rayo sobre velocímetro, degradado violeta-cian)
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\FullOptimizer.ico'))
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $f = $s / 256.0
  # Fondo redondeado con degradado
  $r = [int](56 * $f); if ($r -lt 2) { $r = 2 }
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $w = $s - 1
  $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90); $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
  $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90); $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90); $path.CloseFigure()
  $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(124, 92, 255)), ([System.Drawing.Color]::FromArgb(0, 200, 255)), 45.0
  $g.FillPath($bg, $path)
  # Arco de velocímetro
  if ($s -ge 32) {
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), ([float](18 * $f))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, [float](40 * $f), [float](48 * $f), [float](176 * $f), [float](176 * $f), 150, 240)
  }
  # Rayo
  $pts = @(@(146, 30), @(70, 142), @(122, 142), @(104, 228), @(188, 108), @(134, 108), @(160, 30)) | % { New-Object System.Drawing.PointF ([float]($_[0] * $f)), ([float]($_[1] * $f)) }
  $sh = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 0, 0, 40))
  $g.TranslateTransform([float](6 * $f), [float](6 * $f)); $g.FillPolygon($sh, $pts); $g.ResetTransform()
  $g.FillPolygon([System.Drawing.Brushes]::White, $pts)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += , $ms.ToArray(); $bmp.Dispose()
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
  $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "Icono generado: $Out"
