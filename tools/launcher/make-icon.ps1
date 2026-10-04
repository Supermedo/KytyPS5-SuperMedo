# Draws the launcher's icon (a blue rounded tile with a letter) as a multi-size app.ico.
param([string]$Letter = 'S', [string]$Out = "$PSScriptRoot\app.ico")
Add-Type -AssemblyName System.Drawing
$sizes = 256, 48, 32, 16
$pngs = foreach ($s in $sizes) {
	$bmp = New-Object System.Drawing.Bitmap $s, $s
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.SmoothingMode = 'AntiAlias'
	$g.TextRenderingHint = 'AntiAliasGridFit'
	$r = [math]::Max(2, [int]($s * 0.18)); $d = $r * 2; $w = $s - 1
	$path = New-Object System.Drawing.Drawing2D.GraphicsPath
	$path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
	$path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
	$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new($s, $s)),
		([System.Drawing.Color]::FromArgb(255, 40, 140, 255)), ([System.Drawing.Color]::FromArgb(255, 20, 60, 170))
	$g.FillPath($brush, $path)
	$font = New-Object System.Drawing.Font 'Segoe UI', ([single]($s * 0.62)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
	$format = New-Object System.Drawing.StringFormat
	$format.Alignment = 'Center'; $format.LineAlignment = 'Center'
	$g.DrawString($Letter, $font, [System.Drawing.Brushes]::White, ([System.Drawing.RectangleF]::new(0, [single]($s * 0.02), $s, $s)), $format)
	$g.Dispose()
	$ms = New-Object System.IO.MemoryStream
	$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
	$bmp.Dispose()
	, $ms.ToArray()
}
# ICO with PNG entries: header, one directory entry per size, then the images.
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
	$s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
	$bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
	$bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
	$offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush()
[IO.File]::WriteAllBytes($Out, $ico.ToArray())
"wrote $Out"
