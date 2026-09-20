$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$taskRoot = Split-Path $PSScriptRoot -Parent
$taskIco = Join-Path $taskRoot 'src-modern\Resources\TorreRemota.ico'
$taskPng = Join-Path $taskRoot 'docs\media\remote-pc-bridge-icon.png'
New-Item -ItemType Directory -Force -Path (Split-Path $taskIco), (Split-Path $taskPng) | Out-Null

function New-TaskIconBitmap([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)

    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(2, 2, 20, 20, 180, 90)
    $shape.AddArc(42, 2, 20, 20, 270, 90)
    $shape.AddArc(42, 42, 20, 20, 0, 90)
    $shape.AddArc(2, 42, 20, 20, 90, 90)
    $shape.CloseFigure()
    $blue = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(23, 102, 181))
    $white = [Drawing.Pen]::new([Drawing.Color]::White, 3)
    $green = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(78, 221, 178))
    try {
        $graphics.FillPath($blue, $shape)
        $graphics.DrawRectangle($white, 14, 16, 36, 26)
        $graphics.DrawLine($white, 25, 48, 39, 48)
        $graphics.DrawLine($white, 32, 42, 32, 48)
        $graphics.FillEllipse($green, 45, 44, 9, 9)
    }
    finally {
        $graphics.Dispose()
        $shape.Dispose()
        $blue.Dispose()
        $white.Dispose()
        $green.Dispose()
    }
    return $bitmap
}

$small = New-TaskIconBitmap 64
$stream = [IO.MemoryStream]::new()
try {
    $small.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $writer = [IO.BinaryWriter]::new([IO.File]::Open($taskIco, [IO.FileMode]::Create))
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]1)
        $writer.Write([byte]64)
        $writer.Write([byte]64)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]22)
        $writer.Write($bytes)
    }
    finally { $writer.Dispose() }
}
finally {
    $stream.Dispose()
    $small.Dispose()
}

$large = New-TaskIconBitmap 512
try { $large.Save($taskPng, [Drawing.Imaging.ImageFormat]::Png) }
finally { $large.Dispose() }
