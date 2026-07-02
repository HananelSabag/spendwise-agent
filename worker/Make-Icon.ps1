# ============================================================================
#  Make-Icon.ps1 - builds a real multi-resolution .ico from the SpendWise
#  logo PNG (the "favicon.ico"/"spendwise.jpg" files in this repo are
#  actually 1024x1024 PNGs with a misleading extension - GDI+'s Icon
#  loader rejects those outright, which is why the worker never showed a
#  logo). This writes a proper Vista-style ICO (PNG-compressed frames)
#  containing 16/32/48/256px versions, high-quality downscaled.
#
#  Run once (or whenever the source logo changes):
#    powershell -ExecutionPolicy Bypass -File worker\Make-Icon.ps1
# ============================================================================

Add-Type -AssemblyName System.Drawing

$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Source = Join-Path $WorkerDir 'logo-source.png'   # 1024x1024, transparent bg
$Output = Join-Path $WorkerDir 'spendwise.ico'

if (-not (Test-Path $Source)) { Write-Error "Source logo not found: $Source"; exit 1 }

function Get-ResizedPngBytes([System.Drawing.Image]$src, [int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $bmp.SetResolution($src.HorizontalResolution, $src.VerticalResolution)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CompositingMode   = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.DrawImage($src, 0, 0, $size, $size)
  $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return ,$ms.ToArray()
}

$src = [System.Drawing.Image]::FromFile($Source)
$sizes = @(16, 32, 48, 256)
$frames = @()
foreach ($s in $sizes) { $frames += ,(Get-ResizedPngBytes $src $s) }
$src.Dispose()

# -- Write ICO container (ICONDIR + ICONDIRENTRY[] + PNG payloads) ----------
$fs = [System.IO.File]::Open($Output, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR: reserved(u16)=0, type(u16)=1, count(u16)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$headerSize = 6 + (16 * $sizes.Count)
$offset = $headerSize
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $data = $frames[$i]
  $wByte = if ($s -ge 256) { 0 } else { $s }   # 0 means 256 in ICO format
  $bw.Write([Byte]$wByte)          # width
  $bw.Write([Byte]$wByte)          # height
  $bw.Write([Byte]0)               # color count (0 = no palette, true color)
  $bw.Write([Byte]0)               # reserved
  $bw.Write([UInt16]1)             # color planes
  $bw.Write([UInt16]32)            # bits per pixel
  $bw.Write([UInt32]$data.Length)  # size of image data
  $bw.Write([UInt32]$offset)       # offset of image data
  $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write($data) }

$bw.Flush(); $bw.Close(); $fs.Close()

Write-Output "Wrote $Output ($($sizes -join '/')px, $((Get-Item $Output).Length) bytes)"
