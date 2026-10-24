param([string]$Out)
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  $m = $s * 0.04
  $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 14, 14, 19))
  $g.FillEllipse($bg, $m, $m, $s - 2 * $m, $s - 2 * $m)
  $w = [Math]::Max(1.5, $s * 0.13)
  $r = $s * 0.30
  $c = $s / 2
  $colors = @([System.Drawing.Color]::FromArgb(255, 37, 211, 102), [System.Drawing.Color]::FromArgb(255, 234, 67, 53), [System.Drawing.Color]::FromArgb(255, 255, 138, 61))
  for ($i = 0; $i -lt 3; $i++) {
    $pen = New-Object System.Drawing.Pen $colors[$i], $w
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, $c - $r, $c - $r, 2 * $r, 2 * $r, -90 + $i * 120 + 14, 92)
  }
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += , $ms.ToArray()
  if ($s -eq 256) { $bmp.Save([System.IO.Path]::ChangeExtension($Out, '.png')) }
}
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $d = $pngs[$i]
  $b = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
  $offset += $d.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Close()
