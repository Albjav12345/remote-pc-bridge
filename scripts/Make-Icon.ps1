$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$taskOutput=Join-Path (Split-Path $PSScriptRoot -Parent) 'src-modern\Resources\TorreRemota.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $taskOutput) | Out-Null
$taskBitmap=New-Object Drawing.Bitmap 64,64
$taskGraphics=[Drawing.Graphics]::FromImage($taskBitmap)
$taskGraphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
$taskGraphics.Clear([Drawing.Color]::Transparent)
$taskPath=New-Object Drawing.Drawing2D.GraphicsPath
$taskPath.AddArc(2,2,20,20,180,90);$taskPath.AddArc(42,2,20,20,270,90);$taskPath.AddArc(42,42,20,20,0,90);$taskPath.AddArc(2,42,20,20,90,90);$taskPath.CloseFigure()
$taskBlue=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(23,102,181))
$taskGraphics.FillPath($taskBlue,$taskPath)
$taskWhite=New-Object Drawing.Pen ([Drawing.Color]::White),3
$taskGraphics.DrawRectangle($taskWhite,14,16,36,26)
$taskGraphics.DrawLine($taskWhite,25,48,39,48)
$taskGraphics.DrawLine($taskWhite,32,42,32,48)
$taskGreen=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(78,221,178))
$taskGraphics.FillEllipse($taskGreen,45,44,9,9)
$taskStream=New-Object IO.MemoryStream
$taskBitmap.Save($taskStream,[Drawing.Imaging.ImageFormat]::Png)
$taskPng=$taskStream.ToArray()
$taskWriter=New-Object IO.BinaryWriter ([IO.File]::Open($taskOutput,[IO.FileMode]::Create))
$taskWriter.Write([uint16]0);$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]1)
$taskWriter.Write([byte]64);$taskWriter.Write([byte]64);$taskWriter.Write([byte]0);$taskWriter.Write([byte]0)
$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]32);$taskWriter.Write([uint32]$taskPng.Length);$taskWriter.Write([uint32]22);$taskWriter.Write($taskPng)
$taskWriter.Close();$taskStream.Close();$taskGraphics.Dispose();$taskBitmap.Dispose();$taskPath.Dispose();$taskBlue.Dispose();$taskWhite.Dispose();$taskGreen.Dispose()
