# Builds the store/listing banner (1300 x 372) for Nexus Mods / itch.io and similar.
#
#   powershell -ExecutionPolicy Bypass -File scripts\make-nexus-media.ps1
#
# Output: artifacts\media\nexus-banner.png
# (The 1920x1080 screenshots are produced by the NexusMediaTests screenshot tests.)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo   = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $repo 'src\SCMW.App\Assets'
$out    = Join-Path $repo 'artifacts\media'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$W = 1300; $H = 372
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# Dark panel
$g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#12100E'))), 0, 0, $W, $H)

# Right: a slice of the hero (sunset + watchtower + forest), avoiding the wordmark
$hero = [System.Drawing.Image]::FromFile((Join-Path $assets 'hero.png'))
$src = New-Object System.Drawing.Rectangle(1180, 190, 356, 372)
$dst = New-Object System.Drawing.Rectangle(944, 0, 356, 372)
$g.DrawImage($hero, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)

# Fade the image into the panel
$seam = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
  (New-Object System.Drawing.Point(840, 0)), (New-Object System.Drawing.Point(1140, 0)),
  [System.Drawing.Color]::FromArgb(255, 18, 16, 14), [System.Drawing.Color]::FromArgb(0, 18, 16, 14))
$g.FillRectangle($seam, 840, 0, 300, $H)

# Emblem
$emblem = [System.Drawing.Image]::FromFile((Join-Path $assets 'scmw.png'))
$g.DrawImage($emblem, 44, 61, 250, 250)

# Wordmark + descriptor
$white  = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#F2EDE6'))
$copper = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#E07B39'))
$dim    = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#CFC8BE'))
$g.DrawString('SCMW',      (New-Object System.Drawing.Font('Segoe UI', 64, [System.Drawing.FontStyle]::Bold,    [System.Drawing.GraphicsUnit]::Pixel)), $white,  326, 72)
$g.DrawString('LONE WOLF', (New-Object System.Drawing.Font('Segoe UI', 40, [System.Drawing.FontStyle]::Bold,    [System.Drawing.GraphicsUnit]::Pixel)), $copper, 330, 164)
$g.DrawString('SCUM MOD MAKING TOOL', (New-Object System.Drawing.Font('Segoe UI', 20, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)), $dim, 334, 236)

$file = Join-Path $out 'nexus-banner.png'
$bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $hero.Dispose(); $emblem.Dispose()

Write-Host "Saved $file (${W}x${H})"
