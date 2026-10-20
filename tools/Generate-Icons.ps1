# SPDX-License-Identifier: GPL-3.0-or-later
# Original code-drawn PanelWorks icons; no third-party image assets.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
foreach ($role in @('Editor', 'Host')) {
    $images = @()
    foreach ($size in @(16, 24, 32, 48, 64, 256)) {
        $bitmap = New-Object Drawing.Bitmap($size, $size)
        $g = [Drawing.Graphics]::FromImage($bitmap)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.ScaleTransform($size / 64.0, $size / 64.0)
        $dark = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(20, 31, 52))
        $accent = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(45, 212, 191))
        $white = New-Object Drawing.SolidBrush([Drawing.Color]::White)
        try {
            $g.Clear([Drawing.Color]::Transparent)
            $g.FillRectangle($dark, 3, 7, 58, 43)
            $g.FillRectangle($accent, 8, 12, 48, 31)
            $g.FillRectangle($dark, 27, 49, 10, 7)
            $g.FillRectangle($dark, 17, 55, 30, 4)
            if ($role -eq 'Host') {
                $g.FillPolygon($white, [Drawing.PointF[]]@(
                    [Drawing.PointF]::new(25, 17), [Drawing.PointF]::new(25, 38), [Drawing.PointF]::new(43, 27.5)))
            } else {
                $g.FillRectangle($white, 13, 18, 15, 4)
                $g.FillRectangle($white, 13, 27, 10, 4)
                $g.FillPolygon($dark, [Drawing.PointF[]]@(
                    [Drawing.PointF]::new(31, 32), [Drawing.PointF]::new(46, 17),
                    [Drawing.PointF]::new(51, 22), [Drawing.PointF]::new(36, 37), [Drawing.PointF]::new(29, 39)))
            }
            $stream = New-Object IO.MemoryStream
            try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png); $images += ,$stream.ToArray() }
            finally { $stream.Dispose() }
        } finally { $g.Dispose(); $bitmap.Dispose(); $dark.Dispose(); $accent.Dispose(); $white.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $assetDir ("PanelWorks." + $role + ".ico")))
    $writer = New-Object IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
        $offset = 6 + 16 * $images.Count
        $sizes = @(16, 24, 32, 48, 64, 256)
        for ($i = 0; $i -lt $images.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
    } finally { $writer.Dispose(); $file.Dispose() }
}
