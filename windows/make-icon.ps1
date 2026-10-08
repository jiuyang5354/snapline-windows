$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$backgroundBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 39, 49))
$linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 230, 237, 243), 2)
$accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 121, 224, 192))
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$graphics.FillEllipse($backgroundBrush, 2, 2, 60, 60)
$graphics.DrawBezier($linePen, 10, 21, 25, 26, 40, 26, 54, 21)
$graphics.FillRectangle($whiteBrush, 17, 30, 29, 21)
$graphics.FillRectangle($accentBrush, 21, 34, 21, 13)
$graphics.FillRectangle($whiteBrush, 29, 19, 5, 17)
$iconHandle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)
$iconFile = [System.IO.File]::Create((Join-Path $PSScriptRoot 'Snapline.ico'))
try { $icon.Save($iconFile) } finally { $iconFile.Dispose(); $graphics.Dispose(); $linePen.Dispose(); $backgroundBrush.Dispose(); $accentBrush.Dispose(); $whiteBrush.Dispose(); $icon.Dispose(); $bitmap.Dispose() }
Write-Output 'Created original Snapline icon.'
